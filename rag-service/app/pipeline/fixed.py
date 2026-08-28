"""Pipeline determinista fijo: rewrite → expand → híbrido → RRF → dedupe → rerank →
[guardrail de umbral] → generación streaming → guardrails de salida → auto-verificación.
Emite eventos SSE."""

from __future__ import annotations

import time
from collections.abc import AsyncIterator

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
from app.models import Traza
from app.pipeline.verificacion import (
    _abstencion_por_umbral,
    _verificacion_y_revision,
    evaluar_guardrails_salida,
)
from app.retrieval.engine import RetrievalEngine
from app.retrieval.fusion import format_hint


class Sse:
    @staticmethod
    def evento(nombre: str, datos: dict | str) -> dict:
        return {"evento": nombre, "datos": datos}


async def pipeline_fijo(
    settings: Settings,
    engine: RetrievalEngine,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    dominios: list[str] | None,
    documentos_ids: list[str] | None,
) -> AsyncIterator[dict]:
    traza = Traza(modo="fijo") if settings.enable_pipeline_trace else None
    inicio_total = time.perf_counter()
    modelo_solicitado = settings.chain("generation")[0] if settings.chain("generation") else (None, None)

    if not await engine.hay_documentos_listos(dominios):
        raise SinDocumentosError()

    yield Sse.evento("progress", {"etapa": "recuperacion", "texto": "Buscando en los documentos…"})
    contexto = await engine.run_retrieval(
        pregunta, historial, dominios, documentos_ids, traza, con_reescritura=True
    )
    fuentes = contexto.hits
    yield Sse.evento("progress", {"etapa": "recuperacion", "texto": f"Recuperados {len(fuentes)} fragmentos."})

    # --- guardrail de umbral: contexto débil → abstención directa sin generar ---
    abstencion = _abstencion_por_umbral(settings, contexto.confianza, traza)
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

    # --- verificación en segundo plano (nunca bloqueante para el usuario final) ---
    if not settings.enable_self_verification or not fuentes:
        return

    evaluacion = evaluar_guardrails_salida(settings, contenido_limpio, fuentes, traza)

    async for evento in _verificacion_y_revision(
        settings, llm, pregunta, historial, fuentes, contenido_limpio, traza, hint,
        evaluacion_previa=evaluacion,
    ):
        yield evento


async def _revisar(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    historial: list[dict],
    fuentes,
    critica: str,
    hint: str | None,
) -> str | None:
    """Una única revisión corregida con la crítica específica inyectada en el prompt."""
    try:
        mensajes = construir_prompt_generacion(pregunta, historial, fuentes, hint, critica=critica)
        texto = await llm.complete(settings.chain("generation"), mensajes, temperature=0.1, max_tokens=1200)
        return limpiar_citas_invalidas(texto.strip(), len(fuentes)) or None
    except Exception:  # noqa: BLE001 — la revisión nunca rompe la respuesta visible
        return None
