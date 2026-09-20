"""Piezas compartidas por el pipeline fijo y el agéntico: payload del evento done,
abstención por umbral y revisión con crítica. Vivir aquí evita el import cruzado
de símbolos privados entre fixed, agentic y verificacion."""

from __future__ import annotations

import time

from app.config import Settings
from app.core.llms import LlmClient
from app.generation.generate import (
    FRASE_ABSTENCION,
    construir_prompt_generacion,
    limpiar_citas_invalidas,
)
from app.generation.guardrails import supera_umbral
from app.models import FuenteCard, Hit, Traza


def datos_done(
    settings: Settings,
    llm: LlmClient,
    contenido: str,
    contenido_limpio: str,
    tarjetas: list[FuenteCard],
    traza: Traza | None,
    inicio_generacion: float,
    inicio_total: float,
    clarify: dict | None = None,
    media: list[dict] | None = None,
) -> dict:
    """Payload del evento done con métricas estimadas (idéntico en ambos pipelines)."""
    modelo_solicitado = settings.chain("generation")[0] if settings.chain("generation") else (None, None)
    payload = {
        "content": contenido_limpio,
        "sources": [t.to_dict() for t in tarjetas],
        "trace": traza.to_dict() if traza else None,
        "metrics": {
            "tokens": max(1, len(contenido.split())) if contenido else 0,
            "tokensEstimados": True,
            "generacionMs": int((time.perf_counter() - inicio_generacion) * 1000),
            "totalMs": int((time.perf_counter() - inicio_total) * 1000),
            "modelo": getattr(llm, "ultimo_modelo", None),
            "proveedor": getattr(llm, "ultimo_proveedor", None),
            "fallback": getattr(llm, "ultimo_fallback", False),
            "modeloSolicitado": modelo_solicitado[1],
            "proveedorSolicitado": modelo_solicitado[0],
            "razonamiento": settings.razonamiento,
        },
    }
    if clarify is not None:
        payload["clarify"] = clarify
    if media:
        payload["media"] = media
    return payload


def abstencion_por_umbral(
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


async def aclaracion_si_procede(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    confianza: float | None,
    traza: Traza | None,
    catalogo: list[dict],
) -> dict | None:
    """Abstención o aclaración con chips: solo primera pregunta sin contexto y con toggle."""
    if supera_umbral(confianza, settings.guardrail_umbral_relevancia):
        return None
    if historial or not settings.enable_aclaracion:
        return abstencion_por_umbral(settings, confianza, traza)
    from app.generation.clarify import generar_aclaracion

    aclaracion = await generar_aclaracion(settings, llm, pregunta, catalogo)
    if aclaracion is None:
        return abstencion_por_umbral(settings, confianza, traza)
    if traza:
        traza.agregar(
            "guardrail_umbral",
            confianza=round(confianza, 4) if confianza is not None else None,
            umbral=settings.guardrail_umbral_relevancia,
            resultado="aclaracion",
            opciones=len(aclaracion["opciones"]),
        )
    return {
        "content": aclaracion["pregunta"],
        "sources": [],
        "trace": traza.to_dict() if traza else None,
        "clarify": {"opciones": aclaracion["opciones"]},
    }


async def revisar(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    fuentes: list[Hit],
    critica: str,
    hint: str | None,
) -> str | None:
    """Una única revisión corregida con la crítica específica inyectada en el prompt."""
    try:
        mensajes = construir_prompt_generacion(pregunta, historial, fuentes, hint, critica=critica)
        texto = await llm.complete(settings.chain("generation"), mensajes, temperature=0.1, max_tokens=600)
        return limpiar_citas_invalidas(texto.strip(), len(fuentes)) or None
    except Exception:  # noqa: BLE001 — la revisión nunca rompe la respuesta visible
        return None
