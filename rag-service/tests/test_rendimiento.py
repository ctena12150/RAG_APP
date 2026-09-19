"""Tests de las optimizaciones de latencia y correctitud (fases 1-3).

Sin red: embeddings/LLM falsos deterministas. Verifican que el retrieval
embeddea en lote, reutiliza el vector de la caché y conserva puntuaciones.
"""

from __future__ import annotations

import asyncio
import json

from app.agents.director import crear_herramientas
from app.core.cache import CacheSemantico
from app.models import Hit
from tests.doubles import contenedor_prueba, seed_documentos


def _hit(puntuacion: float = 0.7) -> Hit:
    return Hit(
        chunk_id="c1",
        documento_id="d1",
        documento_nombre="doc.md",
        dominio="rrhh",
        indice=0,
        texto="texto del chunk",
        pagina=None,
        seccion=None,
        puntuacion=puntuacion,
    )


def test_hit_to_dict_conserva_puntuacion():
    """El wire agéntico ya no pierde la puntuación al fusionar pasajes."""
    original = _hit(0.82345)
    reconstruido = Hit.from_dict(original.to_dict())
    assert reconstruido.puntuacion == round(0.82345, 4)
    assert reconstruido.chunk_id == "c1"


def test_cache_replay_sin_traza_ni_metricas():
    """El replay cacheado no sirve tiempos ni modelo de otro turno."""
    cache = CacheSemantico()
    vector = [1.0, 0.0, 0.0]
    cache.guardar(vector, [
        {"evento": "token", "datos": {"t": "hola"}},
        {"evento": "done", "datos": {"content": "hola", "trace": {"modo": "fijo"}, "metrics": {"totalMs": 999}}},
    ])
    replay = cache.buscar(vector)
    assert replay is not None
    done = next(e for e in replay if e["evento"] == "done")
    assert done["datos"]["content"] == "hola"
    assert done["datos"]["trace"] is None
    assert done["datos"]["metrics"] is None


def test_retrieval_embeddea_variantes_en_un_solo_lote():
    """Con expansión activa: un único embed() con todas las variantes."""
    contenedor, emb, llm, store = contenedor_prueba(
        enable_query_expansion=True, query_expansion_count=2, enable_query_rewrite=False
    )
    asyncio.run(seed_documentos(store, "doc-r1", "rrhh", ["La política establece veintitrés días de vacaciones."]))
    llm.encolar(json.dumps({"variantes": ["días libres anuales", "vacaciones pagadas"]}))

    contexto = asyncio.run(contenedor.engine.run_retrieval("vacaciones", [], None, None))

    assert contexto.variantes == ["vacaciones", "días libres anuales", "vacaciones pagadas"]
    assert emb.veces == 1  # antes: una llamada por variante
    assert emb.llamadas == 3
    assert contexto.hits


def test_retrieval_reutiliza_vector_de_la_cache():
    """Primera pregunta sin reescritura: el vector cacheado evita un embed."""
    contenedor, emb, _llm, store = contenedor_prueba(enable_query_rewrite=False)
    asyncio.run(seed_documentos(store, "doc-r2", "rrhh", ["Veintitrés días naturales al año."]))
    vector = asyncio.run(emb.embed(["¿días?"]))
    emb.veces = 0
    emb.llamadas = 0

    contexto = asyncio.run(
        contenedor.engine.run_retrieval("¿días?", [], None, None, embedding_pregunta=vector[0])
    )

    assert emb.veces == 0  # variante única ya calculada: cero llamadas
    assert contexto.hits


def test_retrieval_no_reutiliza_vector_si_hubo_reescritura():
    """Con reescritura efectiva el vector original no vale para la variante 0."""
    contenedor, emb, llm, store = contenedor_prueba(
        enable_query_rewrite=True, enable_query_expansion=True, query_expansion_count=1
    )
    asyncio.run(seed_documentos(store, "doc-r3", "rrhh", ["Veintitrés días naturales al año."]))
    llm.encolar(json.dumps({"consulta": "consulta reescrita autocontenida", "variantes": ["otra formulación"]}))
    vector = asyncio.run(emb.embed(["¿y el segundo?"]))
    emb.veces = 0
    emb.llamadas = 0

    contexto = asyncio.run(
        contenedor.engine.run_retrieval(
            "¿y el segundo?",
            [{"role": "user", "content": "días de vacaciones"}],
            None, None,
            embedding_pregunta=vector[0],
        )
    )

    assert contexto.variantes[0] == "consulta reescrita autocontenida"
    assert len(llm.peticiones) == 1  # reescritura+expansión fusionadas en una llamada
    assert emb.llamadas == 2  # reescrita + variante: el vector original se descarta
    assert contexto.hits


def test_listar_documentos_devuelve_metadatos_reales():
    """La herramienta del Director ya no es un no-op: lista documentos del store."""
    contenedor, _emb, _llm, store = contenedor_prueba()
    asyncio.run(seed_documentos(store, "doc-r4", "rrhh", ["Contenido de prueba."]))
    herramientas, _registro = asyncio.run(crear_herramientas(contenedor.engine, contenedor.settings, None, None))
    listar = next(h for h in herramientas if getattr(h, "name", "") == "listar_documentos")

    resultado = json.loads(asyncio.run(listar.ainvoke({})))

    assert resultado["documentos"]
    assert resultado["documentos"][0]["id"] == "doc-r4"
