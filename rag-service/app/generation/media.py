"""Recursos multimedia extraídos de los chunks citados (no del texto generado).

El nombre del blob (GUID) no dice nada: cada recurso lleva el texto de la
frase que contiene la URL como `contexto`, más la procedencia del chunk.
"""

from __future__ import annotations

import re
from urllib.parse import urlparse

from app.models import Hit

_URL_RE = re.compile(r"https?://[^\s<>\")\]]+", re.IGNORECASE)
_ESPACIOS_RE = re.compile(r"\s+")
_EXTENSIONES_VIDEO = (".mp4", ".webm", ".ogv", ".mov", ".m4v")


def _limpiar_url(url: str) -> str:
    return url.rstrip(".,;:!?)")


def _esquema_valido(url: str) -> bool:
    esquema = urlparse(url).scheme.lower()
    return esquema in ("http", "https")


def _es_video(url: str) -> bool:
    ruta = urlparse(url).path.lower()
    return ruta.endswith(_EXTENSIONES_VIDEO)


def _contexto_para(url_limpia: str, linea: str) -> str:
    sin_url = _ESPACIOS_RE.sub(" ", linea.replace(url_limpia, " ").strip())
    sin_url = re.sub(r"\(\s*\)", "", sin_url).strip()
    return sin_url[:200].strip()


def extraer_media(hits: list[Hit], max_recursos: int = 6) -> list[dict]:
    """Dedupe por URL; conserva el primer contexto no vacío."""
    recursos: list[dict] = []
    vistos: dict[str, dict] = {}
    for hit in hits:
        for linea in hit.texto.splitlines():
            for cruda in _URL_RE.findall(linea):
                url = _limpiar_url(cruda)
                if not url or not _esquema_valido(url):
                    continue
                if url in vistos:
                    actual = vistos[url]
                    if not actual["contexto"]:
                        contexto = _contexto_para(url, linea) or hit.seccion or hit.documento_nombre
                        actual["contexto"] = contexto[:200]
                    continue
                contexto = _contexto_para(url, linea) or hit.seccion or hit.documento_nombre
                recurso = {
                    "url": url,
                    "tipo": "video" if _es_video(url) else "enlace",
                    "proveedor": "azure" if ".blob.core.windows.net" in urlparse(url).netloc.lower() else None,
                    "contexto": contexto[:200],
                    "documento": hit.documento_nombre,
                    "pagina": hit.pagina,
                    "seccion": hit.seccion,
                }
                vistos[url] = recurso
                recursos.append(recurso)
                if len(recursos) >= max_recursos:
                    return recursos
    return recursos
