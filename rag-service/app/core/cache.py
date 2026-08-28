"""Caché semántica de consultas: reutiliza respuestas previas para preguntas
cuyo embedding es prácticamente idéntico (coseno ≥ umbral). Reduce latencia y
consumo de LLM en preguntas repetidas. Se invalida por completo al ingestar o
borrar documentos (el corpus cambia ⇒ las respuestas ya no son fiables).

Diseño: LRU acotado en memoria del proceso, con TTL por entrada. Solo se usa
para la primera pregunta de una conversación (historial vacío): la reescritura
de consultas depende del historial y contaminaría la caché.

La clave es la PAREJA (embedding de la pregunta, contexto): el contexto incluye
dominios, documentIds, mode y overrides del pipeline; dos peticiones con igual
pregunta pero distinto alcance NUNCA comparten entrada.
"""

from __future__ import annotations

import math
import time
from dataclasses import dataclass, field


@dataclass(slots=True)
class _Entrada:
    vector: list[float]
    contexto: str
    eventos: list[dict]          # secuencia SSE completa del turno (sin "meta")
    creado: float = field(default_factory=time.monotonic)


class CacheSemantico:
    """Caché LRU por similitud de coseno entre embeddings de pregunta + contexto."""

    def __init__(self, umbral_similitud: float = 0.95, max_entradas: int = 128, ttl_segundos: int = 3600) -> None:
        self.umbral = umbral_similitud
        self.max_entradas = max(1, max_entradas)
        self.ttl = ttl_segundos
        self._entradas: list[_Entrada] = []
        self.hits = 0

    def buscar(self, vector: list[float], contexto: str = "") -> list[dict] | None:
        """Devuelve los eventos en caché si (vector, contexto) coinciden; si no, None."""
        ahora = time.monotonic()
        mejor_i, mejor_sim = -1, -1.0
        vivas: list[_Entrada] = []
        for entrada in self._entradas:
            if ahora - entrada.creado > self.ttl:
                continue  # caducada: se descarta al reconstruir la lista
            if entrada.contexto != contexto:
                vivas.append(entrada)
                continue  # otro alcance (dominios/documentIds/mode/overrides): no aplica
            vivas.append(entrada)
            sim = _coseno(vector, entrada.vector)
            if sim > mejor_sim:
                mejor_i, mejor_sim = len(vivas) - 1, sim
        self._entradas = vivas
        if mejor_i >= 0 and mejor_sim >= self.umbral:
            self.hits += 1
            entrada = self._entradas.pop(mejor_i)   # mover al frente (LRU)
            self._entradas.insert(len(self._entradas), entrada)
            return [dict(e) for e in entrada.eventos]
        return None

    def guardar(self, vector: list[float], eventos: list[dict], contexto: str = "") -> None:
        """Almacena la secuencia de eventos de un turno; expulsa la más antigua si excede el límite."""
        if not eventos or any(e.get("evento") == "error" for e in eventos):
            return  # nunca se cachean errores
        self._entradas.append(_Entrada(vector=list(vector), contexto=contexto, eventos=[dict(e) for e in eventos]))
        if len(self._entradas) > self.max_entradas:
            self._entradas.pop(0)

    def invalidar(self) -> None:
        """Vacía la caché (tras ingesta o borrado de documentos)."""
        self._entradas.clear()


def _coseno(a: list[float], b: list[float]) -> float:
    if len(a) != len(b) or not a:
        return 0.0
    producto = sum(x * y for x, y in zip(a, b))
    norma_a = math.sqrt(sum(x * x for x in a))
    norma_b = math.sqrt(sum(y * y for y in b))
    if norma_a == 0.0 or norma_b == 0.0:
        return 0.0
    return producto / (norma_a * norma_b)
