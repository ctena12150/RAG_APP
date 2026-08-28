"""Rutas HTTP del servicio RAG: /ingest, /chat (SSE), /documents/{id} y /health."""

from __future__ import annotations

import json
import logging
import time

import httpx
from fastapi import APIRouter, Header, HTTPException
from fastapi.responses import StreamingResponse
from pydantic import AliasChoices, BaseModel, Field

from app.chunking.chunker import chunkear
from app.config import DOMINIOS, Settings
from app.core.errors import RagError, traducir_excepcion_proveedor
from app.models import Segmento

logger = logging.getLogger(__name__)


class SegmentoIn(BaseModel):
    page: int | None = None
    text: str


class IngestRequest(BaseModel):
    # el backend .NET serializa camelCase; se aceptan ambos formatos
    documento_id: str = Field(validation_alias=AliasChoices("documento_id", "documentoId"))
    nombre_archivo: str = Field(validation_alias=AliasChoices("nombre_archivo", "nombreArchivo"))
    dominio: str
    segmentos: list[SegmentoIn]


class ChatTurnIn(BaseModel):
    role: str
    content: str


class ChatRequest(BaseModel):
    question: str
    history: list[ChatTurnIn] = Field(default_factory=list)
    dominios: list[str] | None = None
    document_ids: list[str] | None = Field(
        default=None, validation_alias=AliasChoices("document_ids", "documentIds")
    )
    mode: str = "auto"
    # overrides puntuales del pipeline (solo claves whitelisted; para eval A/B)
    overrides_retrieval: dict[str, bool] | None = Field(
        default=None, validation_alias=AliasChoices("overrides_retrieval", "overridesRetrieval")
    )
    modelo: str | None = None
    razonamiento: str = "off"
    perfil: str | None = None


# whitelist de overrides: clave externa → campo de Settings
OVERRIDES_PERMITIDOS = {
    "hibrida": "enable_hybrid_search",
    "rerank": "enable_reranking",
    "expansion": "enable_query_expansion",
    "reescritura": "enable_query_rewrite",
    "dedupe": "enable_deduplication",
}


def ajustes_con_overrides(base: Settings, overrides: dict[str, bool] | None) -> Settings:
    """Copia de Settings con los overrides permitidos aplicados; el resto se ignora."""
    if not overrides:
        return base
    aplicables = {
        campo: bool(valor)
        for clave, valor in overrides.items()
        if (campo := OVERRIDES_PERMITIDOS.get(clave)) is not None
    }
    return base.model_copy(update=aplicables) if aplicables else base


def ajustes_con_perfil(base: Settings, perfil: str | None) -> Settings:
    """Aplica perfiles de coste por petición sin modificar la configuración global."""
    if (perfil or "").strip().lower() != "fast":
        return base
    return base.model_copy(update={
        "enable_query_expansion": False,
        "enable_reranking": False,
        "enable_self_verification": False,
        "enable_adaptive_topk": False,
        "retrieval_top_k": 4,
    })


def contexto_cache(request: ChatRequest) -> str:
    """Huella del alcance de la petición: dos turnos solo comparten caché si coincide TODO."""
    return "|".join([
        ",".join(sorted(request.dominios or [])),
        ",".join(sorted(request.document_ids or [])),
        request.mode or "auto",
        json.dumps(request.overrides_retrieval or {}, sort_keys=True, ensure_ascii=False),
    ])


def crear_router(contenedor) -> APIRouter:
    router = APIRouter()
    settings: Settings = contenedor.settings

    def _verificar_clave(x_internal_key: str | None) -> None:
        if settings.internal_api_key and x_internal_key != settings.internal_api_key:
            raise HTTPException(status_code=401, detail="Clave interna ausente o inválida.")

    async def _ingesta(request: IngestRequest) -> dict:
        if request.dominio not in DOMINIOS:
            raise HTTPException(
                status_code=400,
                detail=f"Dominio '{request.dominio}' no válido. Permitidos: {', '.join(DOMINIOS)}",
            )
        segmentos = [Segmento(page=s.page, text=s.text) for s in request.segmentos]
        texto_total = "\n".join(s.text for s in segmentos)
        if len(texto_total.strip()) < 20:
            raise HTTPException(status_code=422, detail="El contenido extraído es demasiado corto para indexar.")

        chunks = chunkear(segmentos)
        embeddings = await contenedor.embeddings.embed([c.texto for c in chunks])
        ids = await contenedor.store.reemplazar_chunks(request.documento_id, request.dominio, chunks, embeddings)
        if contenedor.cache is not None:
            contenedor.cache.invalidar()
        logger.info("Ingesta OK: %s → %d chunks", request.nombre_archivo, len(chunks))
        return {
            "chunks": [
                {
                    "indice": c.indice,
                    "texto": c.texto,
                    "pagina": c.pagina,
                    "seccion": c.seccion,
                    "chunkId": cid,
                }
                for c, cid in zip(chunks, ids)
            ]
        }

    @router.post("/ingest")
    async def ingest(request: IngestRequest, X_Internal_Key: str | None = Header(default=None)) -> dict:
        _verificar_clave(X_Internal_Key)
        try:
            return await _ingesta(request)
        except RagError as exc:
            raise HTTPException(status_code=503, detail=exc.mensaje) from exc

    @router.delete("/documents/{documento_id}")
    async def borrar_documento(documento_id: str, X_Internal_Key: str | None = Header(default=None)) -> dict:
        _verificar_clave(X_Internal_Key)
        await contenedor.store.borrar_documento(documento_id)
        if contenedor.cache is not None:
            contenedor.cache.invalidar()
        return {"borrado": True}

    @router.post("/cache/invalidar")
    async def invalidar_cache(X_Internal_Key: str | None = Header(default=None)) -> dict:
        """Vacía la caché semántica tras borrar una conversación: las respuestas
        cacheadas quedan huérfanas y podrían servir contexto no deseado."""
        _verificar_clave(X_Internal_Key)
        if contenedor.cache is not None:
            contenedor.cache.invalidar()
        return {"invalidado": True}

    @router.get("/health")
    async def health() -> dict:
        try:
            docs_listos = await contenedor.store.contar_documentos_listos()
        except Exception:  # noqa: BLE001 — el health nunca debe lanzar
            docs_listos = -1
        return {
            "version": "1.0.0",
            "configuracion": {
                "almacen": settings.rag_store,
                "modo_agentico": settings.enable_agentic_mode,
                "busqueda_hibrida": settings.enable_hybrid_search,
                "reranking": settings.enable_reranking,
                "reescritura": settings.enable_query_rewrite,
                "expansion": settings.enable_query_expansion,
                "dedupe": settings.enable_deduplication,
                "topk_adaptativo": settings.enable_adaptive_topk,
                "auto_verificacion": settings.enable_self_verification,
                "trazas": settings.enable_pipeline_trace,
                "embeddings": settings.embeddings_chain.split(",")[0],
                "generacion": settings.generation_chain.split(",")[0],
                "documentosListos": docs_listos,
            },
        }

    @router.get("/models")
    async def models(X_Internal_Key: str | None = Header(default=None)) -> dict:
        """Lista modelos disponibles sin exponer claves de proveedores."""
        _verificar_clave(X_Internal_Key)
        resultado: list[dict] = []
        try:
            async with httpx.AsyncClient(timeout=8) as client:
                base_ollama = settings.ollama_base_url.removesuffix("/v1").rstrip("/")
                respuesta = await client.get(base_ollama + "/api/tags")
                respuesta.raise_for_status()
                for modelo in respuesta.json().get("models", []):
                    nombre = str(modelo.get("name", ""))
                    capacidades = modelo.get("capabilities", modelo.get("details", {}).get("capabilities", []))
                    es_embedding = "embedding" in capacidades or nombre.startswith("bge-")
                    resultado.append({
                        "proveedor": "ollama",
                        "modelo": nombre,
                        "nombre": nombre,
                        "local": True,
                        "seleccionable": not es_embedding,
                        "capacidades": ["embeddings"] if es_embedding else ["chat", "agentic"],
                    })
        except Exception as exc:  # noqa: BLE001 — el catálogo no debe romper el chat
            logger.warning("No se pudo consultar Ollama para el catálogo: %s", type(exc).__name__)

        if settings.groq_api_key:
            try:
                async with httpx.AsyncClient(timeout=8) as client:
                    respuesta = await client.get(
                        settings.groq_base_url.rstrip("/") + "/models",
                        headers={"Authorization": f"Bearer {settings.groq_api_key}"},
                    )
                    respuesta.raise_for_status()
                    for modelo in respuesta.json().get("data", []):
                        nombre = str(modelo.get("id", ""))
                        no_chat = any(p in nombre.lower() for p in ("whisper", "prompt-guard", "orpheus"))
                        resultado.append({
                            "proveedor": "groq",
                            "modelo": nombre,
                            "nombre": nombre,
                            "local": False,
                            "seleccionable": not no_chat,
                            "capacidades": ["chat"] if not no_chat else [],
                        })
            except Exception as exc:  # noqa: BLE001 — catálogo parcial es válido
                logger.warning("No se pudo consultar Groq para el catálogo: %s", type(exc).__name__)
        return {"modelos": resultado}

    def _sse(nombre: str, datos: dict) -> str:
        return f"event: {nombre}\ndata: {json.dumps(datos, ensure_ascii=False)}\n\n"

    @router.post("/chat")
    async def chat(request: ChatRequest, X_Internal_Key: str | None = Header(default=None)) -> StreamingResponse:
        _verificar_clave(X_Internal_Key)

        async def flujo():
            inicio = time.perf_counter()
            try:
                from app.pipeline.agentic import pipeline_agentic
                from app.pipeline.fixed import pipeline_fijo

                modo = request.mode or "auto"
                usar_agentic = settings.enable_agentic_mode and modo in ("auto", "agentic")
                ajustes = ajustes_con_perfil(settings, request.perfil)
                ajustes = ajustes_con_overrides(ajustes, request.overrides_retrieval)
                if request.razonamiento not in {"off", "low", "medium", "high"}:
                    raise HTTPException(status_code=400, detail="El nivel de razonamiento no es válido.")
                ajustes = ajustes.model_copy(update={"razonamiento": request.razonamiento})
                if request.modelo:
                    proveedor, separador, nombre_modelo = request.modelo.partition(":")
                    if (
                        not separador
                        or proveedor not in {"groq", "ollama"}
                        or not nombre_modelo
                        or "," in nombre_modelo
                        or "\n" in nombre_modelo
                    ):
                        raise HTTPException(status_code=400, detail="El modelo seleccionado no es válido.")
                    seleccion = (proveedor, nombre_modelo)
                    cadena_base = ajustes.chain("generation")
                    cadena = [seleccion, *(paso for paso in cadena_base if paso != seleccion)]
                    ajustes = ajustes.model_copy(
                        update={"generation_chain": ",".join(f"{p}:{m}" for p, m in cadena)}
                    )

                # --- caché semántica: solo primera pregunta (historial vacío) ---
                eventos_cacheados = None
                vector_pregunta: list[float] | None = None
                alcance = contexto_cache(request)
                if contenedor.cache is not None and not request.history:
                    try:
                        (vector_pregunta,) = await contenedor.embeddings.embed([request.question])
                        eventos_cacheados = contenedor.cache.buscar(vector_pregunta, alcance)
                    except Exception:  # noqa: BLE001 — la caché nunca rompe el chat
                        logger.exception("Caché semántica no disponible; se continúa sin ella")
                        vector_pregunta = None
                if eventos_cacheados is not None:
                    logger.info("/chat servido desde caché semántica en %.2fs", time.perf_counter() - inicio)
                    for evento in eventos_cacheados:
                        yield _sse(evento["evento"], evento["datos"])
                    return

                if usar_agentic:
                    # el fallback al pipeline fijo ocurre dentro del generador agéntico,
                    # siempre antes del primer token
                    generador = pipeline_agentic(
                        ajustes,
                        contenedor.engine,
                        contenedor.llm,
                        request.question,
                        [t.model_dump() for t in request.history],
                        request.dominios,
                        request.document_ids,
                    )
                else:
                    generador = pipeline_fijo(
                        ajustes,
                        contenedor.engine,
                        contenedor.llm,
                        request.question,
                        [t.model_dump() for t in request.history],
                        request.dominios,
                        request.document_ids,
                    )

                vistos: list[dict] = []
                async for evento in generador:
                    vistos.append({"evento": evento["evento"], "datos": evento["datos"]})
                    yield _sse(evento["evento"], evento["datos"])

                if contenedor.cache is not None and vector_pregunta is not None and vistos:
                    contenedor.cache.guardar(vector_pregunta, vistos, alcance)
            except RagError as exc:
                yield _sse("error", {"code": exc.codigo, "message": exc.mensaje})
            except Exception as exc:  # noqa: BLE001 — última barrera; mensaje controlado
                logger.exception("Fallo no controlado en /chat")
                error = traducir_excepcion_proveedor(exc, "chat")
                yield _sse("error", {"code": error.codigo, "message": error.mensaje})
            finally:
                logger.info("/chat completado en %.1fs", time.perf_counter() - inicio)

        return StreamingResponse(flujo(), media_type="text/event-stream", headers={"Cache-Control": "no-cache"})

    return router
