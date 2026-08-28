"""Tests unitarios del chunking estructural (deterministas, sin red)."""

from app.chunking.chunker import _cortar_en_frase, _ventanas, chunkear
from app.models import Segmento


def test_chunker_secciona_por_headers_markdown():
    segmentos = [
        Segmento(
            page=None,
            text="# Vacaciones\nSe solicitan por el portal. Son 23 días.\n## Nómina\nSe abona el día 25.",
        )
    ]
    chunks = chunkear(segmentos)
    assert len(chunks) >= 2
    assert any(c.seccion == "Vacaciones" for c in chunks)
    assert any(c.seccion == "Nómina" for c in chunks)
    # el header se conserva en el texto del chunk para contexto del LLM
    assert any(c.texto.startswith("# Nómina") for c in chunks)


def test_chunker_texto_plano_usa_ventanas_con_snap_de_frase():
    texto_largo = ("La caldera requiere revisión semestral. " * 40).strip()
    chunks = chunkear([Segmento(page=3, text=texto_largo)])
    assert len(chunks) > 1
    for c in chunks:
        # ningún chunk corta a mitad de frase: termina en punto o es el último
        assert c.texto.endswith(".") or c is chunks[-1]
        assert all(ch.pagina == 3 for ch in chunks)


def test_corte_de_frase_nunca_deja_minimo_invalido():
    texto = "palabra " * 100 + "Fin de frase."
    corte = _cortar_en_frase(texto[:500])
    assert 0 < corte <= 500


def test_ventanas_respetan_solape():
    texto = "Frase número uno aquí. " * 60
    ventanas = _ventanas(texto, tamano=200, solape=50)
    assert len(ventanas) >= 2


def test_pagina_se_propaga_a_los_chunks():
    chunks = chunkear([Segmento(page=7, text="Texto de la página siete con contenido suficiente.")])
    assert chunks[0].pagina == 7
