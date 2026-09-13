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
from app.generation.generate import verificar
from app.generation.guardrails import EvaluacionGuardrails, evaluar_salida
from app.models import Hit, Traza
from app.pipeline.comun import revisar

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

        # la revisión cita solo las fuentes del done ya emitido: nada de búsquedas
        # extra aquí (renumerarían las citas que el usuario ya vio)
        revision = await revisar(settings, llm, pregunta, historial, fuentes, critica, hint)

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
