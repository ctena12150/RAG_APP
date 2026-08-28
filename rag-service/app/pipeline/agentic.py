"""Modo agéntico: el Director decide búsquedas; comparte guardrails, generación y
verificación con el pipeline fijo. Si la planificación falla ANTES del primer token,
cae de forma transparente al pipeline fijo (nunca se propaga un error al usuario)."""

from __future__ import annotations

import logging
import time
from collections.abc import AsyncIterator

from app.agents.director import ResultadoAgentic, ejecutar_director_stream
from app.config import Settings
from app.core.errors import SinDocumentosError
from app.core.llms import LlmClient
from app.generation.generate import (
    construir_prompt_generacion,
    construir_tarjetas,
    extraer_fuentes_usadas,
    generar_streaming,
    limpiar_citas_invalidas,
)
from app.models import Hit, Traza
from app.pipeline.fixed import Sse
from app.pipeline.verificacion import (
    _abstencion_por_umbral,
    _verificacion_y_revision,
    evaluar_guardrails_salida,
)
from app.retrieval.engine import RetrievalEngine
from app.retrieval.fusion import format_hint

logger = logging.getLogger(__name__)


async def pipeline_agentic(
    settings: Settings,
    engine: RetrievalEngine,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
) -> AsyncIterator[dict]:
    traza = Traza(modo="agentico") if settings.enable_pipeline_trace else None

    if not await engine.hay_documentos_listos(dominios):
        raise SinDocumentosError()

    yield Sse.evento("progress", {"etapa": "inicio", "texto": "Analizando la pregunta…"})
    try:
        inicio = time.perf_counter()
        resultado: ResultadoAgentic | None = None
        async for item in ejecutar_director_stream(
            settings, engine, llm, pregunta, historial, dominios, documentos_ids,
            traza or Traza(modo="agentico"),
        ):
            if isinstance(item, ResultadoAgentic):
                resultado = item
            else:
                # progreso del director en vivo (evento SSE "agent"); nunca tokens
                yield Sse.evento("agent", item)
        if resultado is None:
            raise RuntimeError("el director terminó sin resultado")
        if traza:
            traza.etapas[-1]["duracionMs"] = int((time.perf_counter() - inicio) * 1000)
    except Exception as exc:  # noqa: BLE001 — fallback transparente al modo determinista
        logger.warning("Planificación agéntica falló (%s); fallback a pipeline fijo", exc)
        if traza:
            traza.agregar("fallback_pipeline_fijo", motivo=str(exc)[:120])
        contexto = await engine.run_retrieval(pregunta, historial, dominios, documentos_ids, traza)
        async for evento in _fase_respuesta(
            settings, engine, llm, pregunta, historial, dominios, documentos_ids,
            traza, contexto.hits, contexto.confianza, False,
        ):
            yield evento
        return

    # sin búsqueda decidida por el director (saludo/charla): responder sin fuentes documentales
    async for evento in _fase_respuesta(
        settings, engine, llm, pregunta, historial, dominios, documentos_ids,
        traza, resultado.hits, resultado.confianza,
        settings.enable_agentic_research_on_revision,
    ):
        yield evento


async def _fase_respuesta(
    settings: Settings,
    engine: RetrievalEngine,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
    traza: Traza | None,
    fuentes: list[Hit],
    confianza: float | None,
    permitir_busqueda_extra: bool,
) -> AsyncIterator[dict]:
    modelo_solicitado = settings.chain("generation")[0] if settings.chain("generation") else (None, None)

    # --- guardrail de umbral (solo si hay confianza calculada; fuentes vacías pasan: saludo) ---
    if fuentes:
        abstencion = _abstencion_por_umbral(settings, confianza, traza)
        if abstencion is not None:
            yield Sse.evento("done", abstencion)
            return

    hint = format_hint(pregunta) if settings.enable_format_hints else None
    mensajes = construir_prompt_generacion(pregunta, historial, fuentes, hint)

    inicio_total = time.perf_counter()
    inicio = time.perf_counter()
    yield Sse.evento("progress", {"etapa": "generacion", "texto": "Generando respuesta…"})
    partes: list[str] = []
    async for trozo in generar_streaming(settings, llm, mensajes):
        partes.append(trozo)
        yield Sse.evento("token", {"t": trozo})
    contenido = "".join(partes).strip()
    if traza:
        traza.agregar("generacion", int((time.perf_counter() - inicio) * 1000), caracteres=len(contenido))

    usadas = extraer_fuentes_usadas(contenido, len(fuentes))
    contenido_limpio = limpiar_citas_invalidas(contenido, len(fuentes))
    tarjetas = construir_tarjetas(fuentes, usadas)

    yield Sse.evento(
        "done",
        {
            "content": contenido_limpio,
            "sources": [t.to_dict() for t in tarjetas],
            "trace": traza.to_dict() if traza else None,
            "metrics": {
                "tokens": max(1, len(contenido.split())) if contenido else 0,
                "tokensEstimados": True,
                "generacionMs": int((time.perf_counter() - inicio) * 1000),
                "totalMs": int((time.perf_counter() - inicio_total) * 1000),
                "modelo": getattr(llm, "ultimo_modelo", None),
                "proveedor": getattr(llm, "ultimo_proveedor", None),
                "fallback": getattr(llm, "ultimo_fallback", False),
                "modeloSolicitado": modelo_solicitado[1],
                "proveedorSolicitado": modelo_solicitado[0],
                "razonamiento": settings.razonamiento,
            },
        },
    )

    if not settings.enable_self_verification or not fuentes:
        return

    evaluacion = evaluar_guardrails_salida(settings, contenido_limpio, fuentes, traza)

    async for evento in _verificacion_y_revision(
        settings, llm, pregunta, historial, fuentes, contenido_limpio, traza, hint,
        evaluacion_previa=evaluacion,
        permitir_busqueda_extra=permitir_busqueda_extra,
        engine=engine,
        dominios=dominios,
        documentos_ids=documentos_ids,
    ):
        yield evento


# reexport para compatibilidad con imports existentes en tests
__all__ = ["pipeline_agentic"]
