"""Tests de integración de las rutas HTTP con dobles: sin red, sin Postgres, sin keys."""

import asyncio
import json

import pytest
from fastapi.testclient import TestClient

from app.api.routes import ChatRequest, ajustes_con_overrides
from app.main import crear_app
from tests.doubles import contenedor_prueba, seed_documentos, settings_prueba

CLAVE = {"X-Internal-Key": "test-key"}


@pytest.fixture()
def entorno():
    contenedor, _emb, llm, store = contenedor_prueba()
    app = crear_app(contenedor=contenedor)
    app.state.contenedor = contenedor
    with TestClient(app) as client:
        yield client, llm, store


def _seed(store, doc_id="doc-1", dominio="rrhh", parrafos=None):
    parrafos = parrafos or ["La política establece veintitrés días de vacaciones al año."]
    asyncio.run(seed_documentos(store, doc_id, dominio, parrafos))


def _parsear_sse(texto: str) -> list[dict]:
    eventos = []
    nombre, datos = None, None
    for linea in texto.splitlines():
        if linea.startswith("event:"):
            nombre = linea[6:].strip()
        elif linea.startswith("data:"):
            datos = linea[5:].strip()
        elif linea == "" and nombre is not None:
            try:
                payload = json.loads(datos or "{}")
            except json.JSONDecodeError:
                payload = {}
            eventos.append({"evento": nombre, "datos": payload})
            nombre = None
    return eventos


def test_cache_invalidar_vacia_la_caché(entorno):
    """El endpoint interno /cache/invalidar vacía la caché semántica."""
    client, _, _ = entorno
    app = client.app
    cache = app.state.contenedor.cache
    cache.guardar([1.0, 0.0], [{"evento": "done", "datos": {"content": "a"}}])
    assert cache.buscar([1.0, 0.0]) is not None

    resp = client.post("/cache/invalidar", headers=CLAVE)
    assert resp.status_code == 200
    assert resp.json() == {"invalidado": True}
    assert cache.buscar([1.0, 0.0]) is None


def test_cache_invalidar_exige_clave_interna(entorno):
    client, _, _ = entorno
    assert client.post("/cache/invalidar").status_code == 401


def test_health_reporta_configuracion(entorno):
    client, _, _ = entorno
    resp = client.get("/health")
    assert resp.status_code == 200
    datos = resp.json()
    assert datos["configuracion"]["almacen"] == "memory"
    assert "modo_agentico" in datos["configuracion"]


def test_overrides_solo_aplica_claves_whitelisted():
    base = settings_prueba(enable_hybrid_search=True, enable_reranking=True)
    ajustados = ajustes_con_overrides(
        base, {"hibrida": False, "clave_desconocida": True, "enable_agentic_mode": False}
    )
    assert ajustados.enable_hybrid_search is False        # whitelisted: aplicado
    assert ajustados.enable_reranking is True             # no mencionado: intacto
    assert ajustados.enable_agentic_mode is True          # no whitelisted: ignorado
    # sin overrides devuelve la instancia base
    assert ajustes_con_overrides(base, None) is base


def test_chatrequest_acepta_camelcase_del_backend_dotnet():
    """El .NET serializa camelCase: documentIds y overridesRetrieval deben mapear."""
    req = ChatRequest.model_validate({
        "question": "q",
        "documentIds": ["a", "b"],
        "overridesRetrieval": {"hibrida": False},
    })
    assert req.document_ids == ["a", "b"]
    assert req.overrides_retrieval == {"hibrida": False}


def test_ingesta_valida_indexa_y_devuelve_chunks(entorno):
    client, _, _ = entorno
    resp = client.post(
        "/ingest",
        headers=CLAVE,
        json={
            "documento_id": "11111111-1111-1111-1111-111111111111",
            "nombre_archivo": "politica.md",
            "dominio": "rrhh",
            "segmentos": [{"page": None, "text": "# Vacaciones\nSon 23 días naturales al año."}],
        },
    )
    assert resp.status_code == 200
    assert len(resp.json()["chunks"]) >= 1


def test_ingesta_acepta_camelcase_del_backend_dotnet(entorno):
    """El .NET envía documentoId/nombreArchivo en camelCase: /ingest debe aceptarlos."""
    client, _, _ = entorno
    resp = client.post(
        "/ingest",
        headers=CLAVE,
        json={
            "documentoId": "22222222-2222-2222-2222-222222222222",
            "nombreArchivo": "manual.pdf",
            "dominio": "rrhh",
            "segmentos": [{"page": 1, "text": "# Vacaciones\nSon 23 días naturales al año."}],
        },
    )
    assert resp.status_code == 200
    assert len(resp.json()["chunks"]) >= 1


def test_ingesta_dominio_invalido_400(entorno):
    client, _, _ = entorno
    resp = client.post(
        "/ingest",
        headers=CLAVE,
        json={
            "documento_id": "x",
            "nombre_archivo": "a.txt",
            "dominio": "ventas",
            "segmentos": [{"page": None, "text": "contenido suficientemente largo para validar"}],
        },
    )
    assert resp.status_code == 400


def test_ingesta_sin_clave_interna_401(entorno):
    client, _, _ = entorno
    resp = client.post(
        "/ingest",
        json={"documento_id": "x", "nombre_archivo": "a", "dominio": "rrhh", "segmentos": []},
    )
    assert resp.status_code == 401


def test_borrado_de_documento_limpia_chunks(entorno):
    client, _, store = entorno
    _seed(store)
    resp = client.delete("/documents/doc-1", headers=CLAVE)
    assert resp.status_code == 200
    assert asyncio.run(store.contar_documentos_listos()) == 0


def test_chat_rechaza_controladamente_sin_documentos(entorno):
    client, _, _ = entorno
    resp = client.post("/chat", headers=CLAVE, json={"question": "¿Días de vacaciones?", "mode": "fixed"})
    assert resp.status_code == 200  # el error viaja como evento SSE controlado
    eventos = _parsear_sse(resp.text)
    assert eventos[0]["evento"] == "error"
    assert eventos[0]["datos"]["code"] == "sin_documentos"


def test_chat_flujo_completo_con_fuentes_y_traza(entorno):
    client, llm, store = entorno
    _seed(store)
    llm.encolar("Hay veintitrés días de vacaciones (Fuente 1).")

    resp = client.post(
        "/chat",
        headers=CLAVE,
        json={"question": "¿Cuántos días de vacaciones tengo?", "mode": "fixed"},
    )
    assert resp.status_code == 200
    eventos = _parsear_sse(resp.text)
    tipos = [e["evento"] for e in eventos]
    assert "token" in tipos and "done" in tipos

    done = next(e for e in eventos if e["evento"] == "done")["datos"]
    assert "(Fuente 1)" in done["content"]
    usadas = [s for s in done["sources"] if s["usada"]]
    assert len(usadas) == 1
    assert done["trace"]["modo"] == "fijo"
    etapas = [e["etapa"] for e in done["trace"]["etapas"]]
    assert "busqueda_hibrida" in etapas


def test_chat_con_historial_dispara_reescritura(entorno):
    client, llm, store = entorno
    _seed(store)
    llm.encolar("consulta autónoma reescrita")  # rewrite
    llm.encolar("Hay veintitrés días (Fuente 1).")  # generación

    resp = client.post(
        "/chat",
        headers=CLAVE,
        json={
            "question": "¿y cuánto pagan por cada una?",
            "history": [{"role": "user", "content": "¿Cuántas vacaciones hay?"}],
            "mode": "fixed",
        },
    )
    assert resp.status_code == 200
    eventos = _parsear_sse(resp.text)
    assert any(e["evento"] == "done" for e in eventos)
    # la primera llamada al LLM fue la reescritura (contiene el historial)
    primera = llm.peticiones[0]
    assert any("Historial" in m.get("content", "") for m in primera)


def test_chat_agentic_fallback_a_fijo_cuando_el_planificador_falla(entorno):
    """Con modo agéntico y planificador roto, la respuesta se produce igualmente."""

    client, llm, store = entorno
    _seed(store)
    # el director usa ChatOpenAI real → sin key/red fallará; la cadena generation del LlmFalso responde
    llm.encolar("Respuesta tras fallback (Fuente 1).")

    resp = client.post(
        "/chat",
        headers=CLAVE,
        json={"question": "¿Vacaciones?", "mode": "auto"},
    )
    assert resp.status_code == 200
    eventos = _parsear_sse(resp.text)
    done = next((e for e in eventos if e["evento"] == "done"), None)
    assert done is not None, "El fallback debía producir un done"
    # la traza conserva el modo agéntico pero registra la caída transparente al fijo
    etapas = [e["etapa"] for e in done["datos"]["trace"]["etapas"]]
    assert "fallback_pipeline_fijo" in etapas
    assert "busqueda_hibrida" in etapas
