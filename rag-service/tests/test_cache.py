"""Tests de la caché semántica de consultas y del warm-up de arranque."""

import asyncio
import json

import pytest
from fastapi.testclient import TestClient

from app.core.cache import CacheSemantico
from app.main import _warmup, crear_app
from tests.doubles import EmbeddingsFalsos, contenedor_prueba, seed_documentos

CLAVE = {"X-Internal-Key": "test-key"}


def _parsear_sse(texto: str) -> list[dict]:
    eventos = []
    nombre, datos = None, None
    for linea in texto.splitlines():
        if linea.startswith("event:"):
            nombre = linea[6:].strip()
        elif linea.startswith("data:"):
            datos = linea[5:].strip()
        elif linea == "" and nombre is not None:
            eventos.append({"evento": nombre, "datos": json.loads(datos or "{}")})
            nombre = None
    return eventos


def _ingestar(client, doc_id="doc-cache-1"):
    return client.post(
        "/ingest",
        headers=CLAVE,
        json={
            "documento_id": doc_id,
            "nombre_archivo": "politica.md",
            "dominio": "rrhh",
            "segmentos": [{"page": None, "text": "# Vacaciones\nSon veintitrés días naturales al año."}],
        },
    )


# --- unidad: CacheSemantico ---


def test_cache_hit_con_vector_identico():
    cache = CacheSemantico()
    vector = [1.0, 0.0, 0.0]
    cache.guardar(vector, [{"evento": "done", "datos": {"content": "respuesta"}}])
    assert cache.buscar(vector) == [{"evento": "done", "datos": {"content": "respuesta"}}]
    assert cache.hits == 1


def test_cache_miss_por_debajo_del_umbral():
    cache = CacheSemantico(umbral_similitud=0.95)
    cache.guardar([1.0, 0.0], [{"evento": "done", "datos": {}}])
    assert cache.buscar([0.0, 1.0]) is None


def test_cache_no_comparte_entre_contextos_distintos():
    """Misma pregunta con otro dominio/documentIds/overrides NO debe servirse desde caché."""
    cache = CacheSemantico()
    eventos = [{"evento": "done", "datos": {"content": "de rrhh"}}]
    cache.guardar([1.0, 0.0], eventos, contexto="rrhh||auto|{}")
    assert cache.buscar([1.0, 0.0], contexto="onboarding||auto|{}") is None
    assert cache.buscar([1.0, 0.0], contexto="rrhh|doc-9|auto|{}") is None
    assert cache.buscar([1.0, 0.0], contexto='rrhh||fixed|{"hibrida": false}') is None
    assert cache.buscar([1.0, 0.0], contexto="rrhh||auto|{}") == eventos  # el exacto sí
    assert cache.hits == 1


def test_cache_no_guarda_errores_y_invalidar_vacia():
    cache = CacheSemantico()
    cache.guardar([1.0, 0.0], [{"evento": "error", "datos": {}}])
    assert cache.buscar([1.0, 0.0]) is None
    cache.guardar([1.0, 0.0], [{"evento": "done", "datos": {}}])
    cache.invalidar()
    assert cache.buscar([1.0, 0.0]) is None


def test_cache_lru_expulsa_la_mas_antigua():
    cache = CacheSemantico(max_entradas=2)
    cache.guardar([1.0, 0.0], [{"evento": "done", "datos": {"id": 1}}])
    cache.guardar([0.0, 1.0], [{"evento": "done", "datos": {"id": 2}}])
    cache.buscar([1.0, 0.0])  # refresca la primera → la segunda pasa a ser la antigua
    cache.guardar([0.7, 0.7], [{"evento": "done", "datos": {"id": 3}}])
    assert cache.buscar([0.0, 1.0]) is None
    assert cache.buscar([1.0, 0.0]) is not None


# --- integración: /chat servido desde caché ---


@pytest.fixture()
def entorno_cache():
    # sin reescritura: la utilidad del LLM consumiría la cola de respuestas encoladas
    contenedor, emb, llm, store = contenedor_prueba(enable_cache_consultas=True, enable_query_rewrite=False)
    app = crear_app(contenedor=contenedor)
    with TestClient(app) as client:
        yield client, llm, store, contenedor, emb


def test_chat_repite_pregunta_desde_cache(entorno_cache):
    client, llm, store, contenedor, emb = entorno_cache
    asyncio.run(seed_documentos(store, "doc-cache-1", "rrhh", ["La política establece veintitrés días."]))
    assert _ingestar(client).status_code == 200
    llm.encolar("Son veintitrés días (Fuente 1).")

    body = {"question": "¿Cuántos días de vacaciones hay?", "history": [], "mode": "fixed"}
    primera = _parsear_sse(client.post("/chat", headers=CLAVE, json=body).text)
    assert any(e["evento"] == "done" for e in primera)

    llamadas_llm_antes = len(llm.peticiones)
    segunda = _parsear_sse(client.post("/chat", headers=CLAVE, json=body).text)
    assert segunda == primera
    assert len(llm.peticiones) == llamadas_llm_antes          # el LLM no se volvió a invocar
    assert contenedor.cache.hits == 1
    # la pregunta cacheada costó un embed extra en el lookup
    assert emb.llamadas > 0


def test_cache_no_se_usa_con_historial(entorno_cache):
    client, llm, store, _, _ = entorno_cache
    asyncio.run(seed_documentos(store, "doc-cache-1", "rrhh", ["Veintitrés días al año."]))
    body = {"question": "¿Cuántos días?", "history": [{"role": "user", "content": "hola"}], "mode": "fixed"}
    _parsear_sse(client.post("/chat", headers=CLAVE, json=body).text)
    assert llm.peticiones  # pasó por el pipeline normal


def test_ingesta_invalida_la_cache(entorno_cache):
    client, llm, store, contenedor, _emb = entorno_cache
    asyncio.run(seed_documentos(store, "doc-cache-1", "rrhh", ["Veintitrés días."]))
    llm.encolar("Respuesta A.")
    body = {"question": "¿Cuántos días?", "history": [], "mode": "fixed"}
    _parsear_sse(client.post("/chat", headers=CLAVE, json=body).text)
    assert contenedor.cache.hits == 0

    _ingestar(client)  # invalida por ingesta
    llm.encolar("Respuesta B.")
    _parsear_sse(client.post("/chat", headers=CLAVE, json=body).text)
    assert contenedor.cache.hits == 0  # no sirvió la respuesta anterior


def test_misma_pregunta_otro_dominio_no_usa_cache(entorno_cache):
    """El contexto (dominios) forma parte de la clave: no hay contaminación cruzada."""
    client, llm, store, contenedor, _emb = entorno_cache
    asyncio.run(seed_documentos(store, "doc-cache-1", "rrhh", ["Veintitrés días de vacaciones."]))
    asyncio.run(seed_documentos(store, "doc-cache-2", "onboarding", ["El alta incluye formación inicial."]))
    llm.encolar("Respuesta rrhh.")
    base = {"question": "¿Cuántos días de vacaciones?", "history": [], "mode": "fixed"}
    _parsear_sse(client.post("/chat", headers=CLAVE, json=base).text)

    llm.encolar("Respuesta onboarding.")
    con_otro_dominio = {**base, "dominios": ["onboarding"]}
    eventos = _parsear_sse(client.post("/chat", headers=CLAVE, json=con_otro_dominio).text)
    done = next(e for e in eventos if e["evento"] == "done")
    assert done["datos"]["content"] == "Respuesta onboarding."  # NO la cacheada de rrhh
    assert contenedor.cache.hits == 0


def test_mismos_overrides_distintos_no_usa_cache(entorno_cache):
    """overridesRetrieval forma parte de la clave: los estudios A/B no se contaminan."""
    client, llm, store, contenedor, _emb = entorno_cache
    asyncio.run(seed_documentos(store, "doc-cache-1", "rrhh", ["Veintitrés días de vacaciones."]))
    llm.encolar("Respuesta base.")
    base = {"question": "¿Cuántos días de vacaciones?", "history": [], "mode": "fixed"}
    _parsear_sse(client.post("/chat", headers=CLAVE, json=base).text)

    llm.encolar("Respuesta sin híbrido.")
    ab = {**base, "overridesRetrieval": {"hibrida": False}}
    eventos = _parsear_sse(client.post("/chat", headers=CLAVE, json=ab).text)
    done = next(e for e in eventos if e["evento"] == "done")
    assert done["datos"]["content"] == "Respuesta sin híbrido."
    assert contenedor.cache.hits == 0


def test_cache_desactivada_no_guarda():
    contenedor, _, _, _ = contenedor_prueba(enable_cache_consultas=False)
    assert contenedor.cache is None


# --- warm-up ---


def test_warmup_no_lanza_con_dobles():
    contenedor, _emb, _llm, _store = contenedor_prueba(enable_warmup=True)
    asyncio.run(_warmup(contenedor))  # no debe lanzar aunque el store esté vacío


def test_warmup_tolerante_a_fallos_de_embeddings():
    class EmbRoto(EmbeddingsFalsos):
        async def embed(self, textos):
            raise RuntimeError("proveedor caído")

    contenedor, _, _, _ = contenedor_prueba()
    object.__setattr__(contenedor, "embeddings", EmbRoto())  # doble que no encaja en el tipo real
    asyncio.run(_warmup(contenedor))  # solo loguea, nunca lanza
