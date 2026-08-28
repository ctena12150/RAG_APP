"""Piezas puras del motor de retrieval: RRF, deduplicación por solape de palabras,
top-K adaptativo y parsing del reranker. Todo determinista y testeable sin red."""

from __future__ import annotations

import json
import math
import re

from app.models import Hit

_TOKENIZAR_RE = re.compile(r"[\s,.;:!?()\"'\u00bf\u00a1]+")


def fusion_rrf(listas: list[list[Hit]], k: int = 60) -> list[Hit]:
    """Reciprocal Rank Fusion sobre N listas rankeadas (no solo dos)."""
    puntuaciones: dict[str, float] = {}
    hits: dict[str, Hit] = {}
    for lista in listas:
        for posicion, hit in enumerate(lista):
            clave = hit.chunk_id
            puntuaciones[clave] = puntuaciones.get(clave, 0.0) + 1.0 / (k + posicion + 1)
            hits.setdefault(clave, hit)
    ordenados = sorted(puntuaciones.items(), key=lambda kv: kv[1], reverse=True)
    return [_con_score(hits[clave], score) for clave, score in ordenados]


def _con_score(hit: Hit, score: float) -> Hit:
    clonado = Hit(
        chunk_id=hit.chunk_id,
        documento_id=hit.documento_id,
        documento_nombre=hit.documento_nombre,
        dominio=hit.dominio,
        indice=hit.indice,
        texto=hit.texto,
        pagina=hit.pagina,
        seccion=hit.seccion,
        puntuacion=score,
    )
    return clonado


def _tokens(texto: str) -> set[str]:
    return {t for t in _TOKENIZAR_RE.split(texto.lower()) if len(t) > 2}


def solape_jaccard(a: str, b: str) -> float:
    ta, tb = _tokens(a), _tokens(b)
    if not ta or not tb:
        return 0.0
    interseccion = len(ta & tb)
    return interseccion / len(ta | tb)


def eliminar_duplicados(hits: list[Hit], umbral: float = 0.75) -> tuple[list[Hit], int]:
    """Elimina near-duplicates por solape de palabras (sin llamadas extra a la API)."""
    conservados: list[Hit] = []
    eliminados = 0
    for hit in hits:
        duplicado = any(solape_jaccard(hit.texto, c.texto) >= umbral for c in conservados)
        if duplicado:
            eliminados += 1
            continue
        conservados.append(hit)
    return conservados, eliminados


_BROAD_RE = re.compile(
    r"\b(resum\w*|res\u00fame\w*|compar\w*|diferencias?|general|overview|panorama|"
    r"tod[oa]s los|principales puntos|de que trata|de qu\u00e9 trata)\b",
    re.IGNORECASE,
)


def es_pregunta_amplia(pregunta: str) -> bool:
    return bool(_BROAD_RE.search(pregunta))


def topk_adaptativo(pregunta: str, base: int, bonus: int) -> int:
    """Preguntas amplias (resumir/comparar/overview) recuperan más chunks; coste cero."""
    return base + (bonus if es_pregunta_amplia(pregunta) else 0)


def parsear_respuesta_rerank(texto: str, candidatos: list[Hit]) -> tuple[list[Hit], list[Hit]]:
    """Parsea el JSON del reranker; tolerante a malformaciones (fail-open con aviso)."""
    try:
        inicio, fin = texto.find("{"), texto.rfind("}")
        datos = json.loads(texto[inicio : fin + 1])
        relevantes = [int(i) for i in datos.get("relevantes", [])]
        validos = [i for i in relevantes if isinstance(i, int) and 0 <= i < len(candidatos)]
        if not validos:
            raise ValueError("lista vacía")
        vistos: set[int] = set()
        unicos = [i for i in validos if not (i in vistos or vistos.add(i))]
        mantenidos = [_con_score(candidatos[i], candidatos[i].puntuacion) for i in unicos]
        descartados = [c for j, c in enumerate(candidatos) if j not in set(unicos)]
        return mantenidos, descartados
    except Exception:  # noqa: BLE001 — fail-open: si el rerank falla, pasan todos en orden original
        return list(candidatos), []


def red_de_rescate(pregunta: str, mantenidos: list[Hit], descartados: list[Hit]) -> list[Hit]:
    """Red de seguridad para preguntas amplias: reincorpora los mejores descartados."""
    if not es_pregunta_amplia(pregunta) or not descartados:
        return mantenidos
    extra = sorted(descartados, key=lambda h: h.puntuacion, reverse=True)[: math.ceil(len(descartados) / 2)]
    return mantenidos + extra[:3]


def format_hint(pregunta: str) -> str | None:
    """Pista ligera de formato para preguntas de comparación/pasos/listado (coste cero)."""
    p = pregunta.lower()
    if any(k in p for k in ("compara", "comparación", "diferencia", "vs ", "versus")):
        return "Si la respuesta compara varios elementos, preséntala como tabla markdown."
    if any(k in p for k in ("pasos", "cómo puedo", "como puedo", "procedimiento", "proceso")):
        return "Si describes un procedimiento, usa una lista numerada ordenada."
    if any(k in p for k in ("lista", "enumera", "qué documentos necesito", "requisitos")):
        return "Si enumeras elementos, usa una lista con viñetas."
    return None
