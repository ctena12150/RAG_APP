"""Chunking consciente de estructura: headers markdown primero; ventana de palabras con
recorte anclado a fin de frase como fallback para texto plano/PDF."""

from __future__ import annotations

import re

from app.models import Chunk, Segmento

_TAMANO_OBJETIVO = 900
_SOLAPE = 120
_MINIMO = 60

_HEADER_RE = re.compile(r"^(#{1,6})\s+(.*)$")
_FIN_FRASE_RE = re.compile(r"[.!?…](\s|$)|\n")


def _cortar_en_frase(texto: str) -> int:
    """Índice del final de la frase más cercana ANTES del corte propuesto (nunca corta a mitad)."""
    ultimo = -1
    for m in _FIN_FRASE_RE.finditer(texto, 0):
        ultimo = m.end()
        if m.end() >= len(texto):
            break
    return ultimo if ultimo > _MINIMO else len(texto)


def _ventanas(texto: str, tamano: int = _TAMANO_OBJETIVO, solape: int = _SOLAPE) -> list[str]:
    trozos: list[str] = []
    inicio = 0
    while inicio < len(texto):
        candidato = texto[inicio : inicio + tamano]
        if inicio + tamano < len(texto):
            corte = _cortar_en_frase(candidato)
            candidato = candidato[:corte]
        limpio = candidato.strip()
        if limpio:
            trozos.append(limpio)
        avance = max(len(candidato) - solape if len(candidato) > solape else len(candidato), 1)
        inicio += avance
    return trozos


def chunkear(segmentos: list[Segmento]) -> list[Chunk]:
    """Genera chunks: secciones markdown si existen, ventanas con snap de frase en caso contrario."""
    chunks: list[Chunk] = []
    seccion_actual: str | None = None
    buffer_seccion: list[str] = []
    pagina_seccion: int | None = None

    def cerrar_seccion() -> None:
        nonlocal buffer_seccion
        if not buffer_seccion:
            return
        cuerpo = "\n".join(buffer_seccion).strip()
        for i, ventana in enumerate(_ventanas(cuerpo)):
            chunks.append(
                Chunk(
                    indice=len(chunks),
                    texto=f"# {seccion_actual}\n{ventana}" if seccion_actual else ventana,
                    pagina=pagina_seccion,
                    seccion=seccion_actual,
                )
            )
        buffer_seccion = []

    for segmento in segmentos:
        lineas = segmento.text.splitlines()
        for linea in lineas:
            coincidencia = _HEADER_RE.match(linea.strip())
            if coincidencia:
                cerrar_seccion()
                seccion_actual = coincidencia.group(2).strip()
                pagina_seccion = segmento.page
                buffer_seccion.append(linea.strip())
            else:
                if not buffer_seccion and seccion_actual is None:
                    pagina_seccion = segmento.page
                buffer_seccion.append(linea)
        buffer_seccion.append("")  # separador entre segmentos/páginas

    # sección sin cabecera: el buffer acumuló todo el texto plano
    if not chunks and not buffer_seccion and segmentos:
        for segmento in segmentos:
            for ventana in _ventanas(segmento.text):
                chunks.append(Chunk(indice=len(chunks), texto=ventana, pagina=segmento.page))
        return chunks

    cerrar_seccion()
    return chunks
