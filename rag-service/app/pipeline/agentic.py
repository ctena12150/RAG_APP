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
from app.pipeline.comun import aclaracion_si_procede, datos_done
from app.pipeline.fixed import Sse
from app.pipeline.verificacion import (
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
    embedding_pregunta: list[float] | None = None,
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
        contexto = await engine.run_retrieval(
            pregunta, historial, dominios, documentos_ids, traza,
            embedding_pregunta=embedding_pregunta,
        )
        async for evento in _fase_respuesta(
            settings, llm, pregunta, historial,
            traza, contexto.hits, contexto.confianza, engine,
        ):
            yield evento
        return

    # sin búsqueda decidida por el director (saludo/charla): responder sin fuentes documentales
    async for evento in _fase_respuesta(
        settings, llm, pregunta, historial,
        traza, resultado.hits, resultado.confianza, engine,
    ):
        yield evento


async def _fase_respuesta(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    traza: Traza | None,
    fuentes: list[Hit],
    confianza: float | None,
    engine: RetrievalEngine | None = None,
) -> AsyncIterator[dict]:
    inicio_respuesta = time.perf_counter()

    # --- guardrail de umbral (solo si hay confianza calculada; fuentes vacías pasan: saludo) ---
    if fuentes:
        catalogo = await engine.listar_dominios() if engine else []
        abstencion = await aclaracion_si_procede(
            settings, llm, pregunta, historial, confianza, traza, catalogo
        )
        if abstencion is not None:
            yield Sse.evento("done", abstencion)
            return

    hint = format_hint(pregunta) if settings.enable_format_hints else None
    mensajes = construir_prompt_generacion(pregunta, historial, fuentes, hint)

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
        datos_done(settings, llm, contenido, contenido_limpio, tarjetas, traza, inicio, inicio_respuesta),
    )

    if not settings.enable_self_verification or not fuentes:
        return

    evaluacion = evaluar_guardrails_salida(settings, contenido_limpio, fuentes, traza)

    async for evento in _verificacion_y_revision(
        settings, llm, pregunta, historial, fuentes, contenido_limpio, traza, hint,
        evaluacion_previa=evaluacion,
    ):
        yield evento
