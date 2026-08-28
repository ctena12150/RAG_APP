"""Verificación y revisión compartidas por el pipeline fijo y el agéntico.

Orden barato→caro de los guardrails:
  1. Umbral de relevancia (antes de generar: abstención directa si el contexto es débil)
  2. Guardrails deterministas de salida (datos sin soporte / citas faltantes)
  3. Juez LLM de auto-verificación
Cualquier fallo dispara UNA revisión con la crítica específica inyectada.
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import AsyncIterator

from app.config import Settings
from app.core.llms import LlmClient
from app.generation.generate import FRASE_ABSTENCION, verificar
from app.generation.guardrails import EvaluacionGuardrails, evaluar_salida, supera_umbral
from app.models import Hit, Traza
from app.retrieval.engine import RetrievalEngine

logger = logging.getLogger(__name__)


def evaluar_guardrails_salida(
    settings: Settings,
    respuesta_limpia: str,
    fuentes: list[Hit],
    traza: Traza | None,
) -> EvaluacionGuardrails:
    evaluacion = evaluar_salida(
        respuesta_limpia,
        len(fuentes),
        [h.texto for h in fuentes],
        verificar_datos_activado=settings.guardrail_verificar_datos,
        exigir_citas_activado=settings.guardrail_exigir_citas,
    )
    if traza:
        traza.agregar(
            "guardrails",
            datosSinSoporte=evaluacion.datos_sin_soporte,
            citasFaltantes=evaluacion.citas_faltantes,
        )
    return evaluacion


async def _verificacion_y_revision(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    fuentes: list[Hit],
    contenido_limpio: str,
    traza: Traza | None,
    hint: str | None,
    evaluacion_previa: EvaluacionGuardrails | None = None,
    permitir_busqueda_extra: bool = False,
    engine: RetrievalEngine | None = None,
    dominios: list[str] | None = None,
    documentos_ids: list[str] | None = None,
) -> AsyncIterator[dict]:
    """Juez LLM + guardrails previos → evento verified (+ revision_available si procede)."""
    try:
        critica_guardrail = evaluacion_previa.critica() if evaluacion_previa and evaluacion_previa.hay_problemas else None

        if critica_guardrail:
            # los guardrails deterministas ya fallaron: no hace falta gastar el juez LLM
            veredicto = {"verdict": "unsupported", "critique": critica_guardrail}
        else:
            inicio = time.perf_counter()
            veredicto = await asyncio.wait_for(
                verificar(settings, llm, pregunta, contenido_limpio, fuentes),
                timeout=settings.background_verification_timeout_ms / 1000,
            )
            if traza:
                traza.agregar("verificacion", int((time.perf_counter() - inicio) * 1000), verdicto=veredicto["verdict"])

        if veredicto["verdict"] == "supported":
            yield {"evento": "verified", "datos": {"verdict": "supported"}}
            return

        critica = veredicto.get("critique") or "respuesta no sostenida por las fuentes"

        revision = None
        if permitir_busqueda_extra and engine is not None and fuentes:
            # en modo agéntico: una pequeña búsqueda extra guiada por la crítica
            contexto_extra = await engine.run_retrieval(
                f"{pregunta} {critica[:200]}",
                historial=[],
                dominios=dominios,
                documentos_ids=documentos_ids,
                con_reescritura=False,
            )
            refs_existentes = {h.chunk_id for h in fuentes}
            nuevas = [h for h in contexto_extra.hits if h.chunk_id not in refs_existentes]
            fuentes.extend(nuevas)

        from app.pipeline.fixed import _revisar

        revision = await _revisar(settings, llm, pregunta, historial, fuentes, critica, hint)

        payload_verificado = {
            "verdict": "unsupported",
            "critique": critica,
            "revision": revision,
        }
        if evaluacion_previa and evaluacion_previa.hay_problemas:
            payload_verificado["guardrails"] = {
                "datosSinSoporte": evaluacion_previa.datos_sin_soporte,
                "citasFaltantes": evaluacion_previa.citas_faltantes,
            }
        yield {"evento": "verified", "datos": payload_verificado}
        if revision:
            yield {"evento": "revision_available", "datos": {"revision": revision, "critique": critica}}
    except asyncio.TimeoutError:
        yield {"evento": "verified", "datos": {"verdict": "error"}}
    except Exception as exc:  # noqa: BLE001 — la verificación nunca rompe la respuesta ya mostrada
        logger.warning("Auto-verificación falló (%s); se marca como no verificada", type(exc).__name__)
        yield {"evento": "verified", "datos": {"verdict": "error"}}


def _abstencion_por_umbral(
    settings: Settings,
    confianza: float | None,
    traza: Traza | None,
) -> dict | None:
    """Devuelve el evento `done` de abstención directa si el contexto es demasiado débil."""
    if supera_umbral(confianza, settings.guardrail_umbral_relevancia):
        return None
    if traza:
        traza.agregar(
            "guardrail_umbral",
            confianza=round(confianza, 4) if confianza is not None else None,
            umbral=settings.guardrail_umbral_relevancia,
            resultado="abstencion_directa",
        )
    return {
        "content": FRASE_ABSTENCION,
        "sources": [],
        "trace": traza.to_dict() if traza else None,
    }
