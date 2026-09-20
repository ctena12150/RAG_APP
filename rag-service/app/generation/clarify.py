"""Preguntas de aclaración con opciones clicables (chips).

Se usa SOLO cuando la primera pregunta no recupera contexto suficiente
(guardrail de umbral). Sustituye a la abstención directa en ese caso.
"""

from __future__ import annotations

import json
import logging

from app.config import Settings
from app.core.llms import LlmClient

logger = logging.getLogger(__name__)


def construir_prompt_aclaracion(pregunta: str, catalogo: list[dict]) -> list[dict]:
    ambitos = "\n".join(
        f"- {d.get('clave')}: {d.get('etiqueta', '')} ({d.get('descripcion', '')})".strip()
        for d in catalogo
    )
    return [
        {
            "role": "system",
            "content": (
                "Eres un clasificador de preguntas documentales. Respondes SOLO JSON. "
                "Saludos, despedidas o charla pequeña nunca son ambiguos. "
                'Formato: {"ambigua": true|false, "pregunta": "...", '
                '"opciones": [{"texto": "...", "valor": "..."}]}. '
                "Si ambigua=false, opciones va vacía. Máximo 4 opciones, "
                "cada valor debe ser autocontenido (resuelve referencias). "
                "Responde SIEMPRE en español."
            ),
        },
        {
            "role": "user",
            "content": f"Ámbitos:\n{ambitos or '(sin ámbitos)'}\n\nPregunta: {pregunta}",
        },
    ]


async def generar_aclaracion(
    settings: Settings,
    llm: LlmClient,
    pregunta: str,
    catalogo: list[dict],
) -> dict | None:
    """Devuelve {"pregunta": str, "opciones": [...]} o None si no procede aclarar."""
    try:
        mensajes = construir_prompt_aclaracion(pregunta, catalogo)
        texto = await llm.complete(
            settings.chain("utility"), mensajes, temperature=0.0, max_tokens=300
        )
    except Exception:  # noqa: BLE001 — la aclaración nunca rompe la abstención
        logger.warning("Aclaración no disponible; se mantiene abstención")
        return None
    inicio, fin = texto.find("{"), texto.rfind("}")
    if inicio < 0 or fin < 0:
        return None
    try:
        datos = json.loads(texto[inicio : fin + 1])
    except Exception:  # noqa: BLE001 — JSON inválido equivale a no ambigua
        return None
    if not datos.get("ambigua"):
        return None
    opciones = []
    for op in datos.get("opciones", [])[: settings.aclaracion_max_opciones]:
        if not isinstance(op, dict):
            continue
        texto_op = str(op.get("texto", "")).strip()[:80]
        valor_op = str(op.get("valor", "")).strip()[:200]
        if texto_op and valor_op:
            opciones.append({"texto": texto_op, "valor": valor_op})
    if not opciones:
        return None
    pregunta_txt = str(datos.get("pregunta", "")).strip()[:300] or (
        "Tu pregunta puede referirse a varios temas. ¿Cuál te interesa?"
    )
    return {"pregunta": pregunta_txt, "opciones": opciones}
