"""Tests de RRF, dedupe, top-K adaptativo, rerank-parsing y trazas (sin red)."""

from app.models import Hit
from app.retrieval.fusion import (
    eliminar_duplicados,
    es_pregunta_amplia,
    format_hint,
    fusion_rrf,
    parsear_respuesta_rerank,
    red_de_rescate,
    solape_jaccard,
    topk_adaptativo,
)


def hit(chunk_id: str, texto: str = "texto", score: float = 1.0) -> Hit:
    return Hit(
        chunk_id=chunk_id,
        documento_id="d1",
        documento_nombre="doc.pdf",
        dominio="rrhh",
        indice=0,
        texto=texto,
        pagina=1,
        seccion=None,
        puntuacion=score,
    )


def test_rrf_fusiona_n_listas_y_prioriza_consenso():
    lista_a = [hit("1"), hit("2"), hit("3")]
    lista_b = [hit("2"), hit("4"), hit("5")]
    lista_c = [hit("2"), hit("6")]
    fusion = fusion_rrf([lista_a, lista_b, lista_c])
    assert fusion[0].chunk_id == "2"  # aparece en las tres listas
    # 1º en una lista (1/61+1/62) gana a 2º en dos listas; el orden es determinista por puntuación RRF
    assert [h.chunk_id for h in fusion][:4] == ["2", "1", "4", "6"]
    assert fusion[-1].chunk_id in ("3", "5")


def test_dedupe_elimina_near_duplicates_conservando_el_primero():
    a = hit("a", "La política de vacaciones establece veintitrés días naturales por año.")
    b = hit("b", "La política de vacaciones establece veintitrés días naturales por cada año.")
    c = hit("c", "El mantenimiento de la caldera es semestral y obligatorio.")
    conservados, eliminados = eliminar_duplicados([a, b, c], umbral=0.75)
    assert eliminados == 1
    assert [h.chunk_id for h in conservados] == ["a", "c"]
    assert solape_jaccard(a.texto, b.texto) > 0.75
    assert solape_jaccard(a.texto, c.texto) < 0.2


def test_topk_adaptativo_para_preguntas_amplias():
    assert topk_adaptativo("Hazme un resumen de la política de vacaciones", 6, 4) == 10
    assert topk_adaptativo("¿Cuántos días de vacaciones tengo?", 6, 4) == 6
    assert es_pregunta_amplia("Compara los dos planes de pensiones")
    assert not es_pregunta_amplia("¿Cuál es el salario mínimo del convenio?")


def test_rerank_parser_valido_filtra_indices():
    candidatos = [hit(str(i)) for i in range(5)]
    mantenidos, descartados = parsear_respuesta_rerank('{"relevantes": [4, 0, 9, 0]}', candidatos)
    # índices fuera de rango y duplicados se descartan
    assert [h.chunk_id for h in mantenidos] == ["4", "0"]
    assert len(descartados) == 3


def test_rerank_parser_fail_open_con_salida_malformada():
    candidatos = [hit("x"), hit("y")]
    mantenidos, _ = parsear_respuesta_rerank("esto no es json {roto", candidatos)
    assert len(mantenidos) == 2


def test_red_de_rescate_solo_para_amplias():
    mantenidos = [hit("m")]
    descartados = [hit(f"d{i}", score=i / 10) for i in range(5)]
    ampliado = red_de_rescate("Resúmeme todo", mantenidos, descartados)
    assert len(ampliado) > 1
    normal = red_de_rescate("¿Días de vacaciones?", mantenidos, descartados)
    assert len(normal) == 1


def test_format_hints():
    assert "tabla" in format_hint("Compara el plan A vs plan B")
    assert "numerada" in format_hint("¿Cuáles son los pasos para solicitar vacaciones?")
    assert format_hint("¿Cuántos días tengo?") is None
