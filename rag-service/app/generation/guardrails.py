"""Guardrails deterministas de ajuste de la información.

Tres comprobaciones, de baratas a caras, ejecutadas antes del juez LLM:
1. Umbral de relevancia: si la confianza de recuperación (score RRF normalizado 0..1)
   no alcanza el mínimo configurado, se responde directamente con la frase de
   abstención sin gastar una llamada de generación.
2. Verificador de datos: toda cifra/fecha/unidad citada en la respuesta debe existir
   en los textos de las fuentes; un dato inventado dispara la revisión.
3. Exigencia de citas: con fuentes disponibles, la respuesta debe citar al menos una
   (Fuente N) válida, salvo que sea exactamente la abstención.

Ninguno bloquea el streaming ya mostrado: sus fallos entran por el mismo camino de
revisión sugerida que la auto-verificación LLM.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from app.generation.generate import FRASE_ABSTENCION, extraer_fuentes_usadas

# horas (9:30), porcentajes y números con unidad opcional; formatos españoles (7,5)
_DATO_RE = re.compile(
    r"\b\d{1,2}:\d{2}\b"
    r"|\b\d+(?:[.,]\d+)?\s?(?:%|bar\b|V\b|mm\b|cm\b|km\b|kg\b|kWh\b|litros?\b|horas?\b|h\b|d[ií]as?\b|meses\b|mes\b|a\u00f1os?\b)"
    r"|\b\d+(?:[.,]\d+)+\b"
    r"|\b\d{1,4}\b",
    re.IGNORECASE,
)

_CITA_RE = re.compile(r"\(Fuente\s+\d{1,2}\)", re.IGNORECASE)

_NORMALIZAR_ESPACIOS = re.compile(r"\s+")


def _normalizar(texto: str) -> str:
    t = texto.lower().replace(",", ".")
    t = _NORMALIZAR_ESPACIOS.sub(" ", t)
    return t.replace(" .", ".").strip()


def extraer_datos(texto_respuesta: str) -> list[str]:
    """Cifras/fechas/uniones presentes en la respuesta; las citas (Fuente N) se ignoran."""
    sin_citas = _CITA_RE.sub(" ", texto_respuesta)
    return [m.group(0).strip() for m in _DATO_RE.finditer(sin_citas)]


def verificar_datos(texto_respuesta: str, textos_fuentes: list[str]) -> list[str]:
    """Devuelve los datos de la respuesta que NO aparecen en ninguna fuente."""
    datos = extraer_datos(texto_respuesta)
    if not datos or not textos_fuentes:
        return []
    corpus = _normalizar(" \n".join(textos_fuentes))
    return [d for d in datos if _normalizar(d) not in corpus]


@dataclass(slots=True)
class EvaluacionGuardrails:
    datos_sin_soporte: list[str] = field(default_factory=list)
    citas_faltantes: bool = False

    @property
    def hay_problemas(self) -> bool:
        return bool(self.datos_sin_soporte) or self.citas_faltantes

    def critica(self) -> str:
        partes: list[str] = []
        if self.datos_sin_soporte:
            partes.append("estas cifras no aparecen en las fuentes: " + ", ".join(self.datos_sin_soporte))
        if self.citas_faltantes:
            partes.append("la respuesta no cita ninguna fuente (Fuente N)")
        return "; ".join(partes)


def evaluar_salida(
    respuesta_limpia: str,
    n_fuentes: int,
    textos_fuentes: list[str],
    *,
    verificar_datos_activado: bool = True,
    exigir_citas_activado: bool = True,
) -> EvaluacionGuardrails:
    """Evalúa los guardrails de salida sobre la respuesta ya limpia de citas inválidas."""
    evaluacion = EvaluacionGuardrails()

    es_abstencion = FRASE_ABSTENCION.lower() in respuesta_limpia.lower()
    if es_abstencion:
        # la abstención correcta nunca es un fallo de guardrail
        return evaluacion

    if verificar_datos_activado:
        evaluacion.datos_sin_soporte = verificar_datos(respuesta_limpia, textos_fuentes)

    if exigir_citas_activado and n_fuentes > 0:
        evaluacion.citas_faltantes = len(extraer_fuentes_usadas(respuesta_limpia, n_fuentes)) == 0

    return evaluacion


def supera_umbral(confianza: float | None, umbral: float) -> bool:
    """True si la confianza normalizada supera el mínimo (o si el guardrail está apagado)."""
    if umbral <= 0:
        return True
    if confianza is None:
        return True  # modo agéntico sin confianza calculada: otros guardrails cubren la salida
    return confianza >= umbral
