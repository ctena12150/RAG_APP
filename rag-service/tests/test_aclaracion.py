"""Aclaración con chips: solo primera pregunta sin contexto."""

from __future__ import annotations

import asyncio

from app.models import Traza
from app.pipeline.comun import aclaracion_si_procede, datos_done
from tests.doubles import contenedor_prueba


def test_datos_done_incluye_clarify_opcional() -> None:
    contenedor, _, llm, _ = contenedor_prueba()
    payload = datos_done(
        contenedor.settings, llm, "texto", "texto", [], Traza(modo="fijo"), 0.0, 0.0,
        clarify={"opciones": [{"texto": "Hardware", "valor": "Es hardware"}]},
    )
    assert payload["clarify"]["opciones"][0]["texto"] == "Hardware"
    sin = datos_done(contenedor.settings, llm, "t", "t", [], None, 0.0, 0.0)
    assert "clarify" not in sin


def test_no_aclara_con_contexto_fuerte() -> None:
    contenedor, _, llm, _ = contenedor_prueba()
    out = asyncio.run(
        aclaracion_si_procede(
            contenedor.settings, llm, "¿vacaciones?", [], 0.9, Traza(modo="fijo"), []
        )
    )
    assert out is None
    assert not llm.peticiones


def test_solo_primera_pregunta() -> None:
    contenedor, _, llm, _ = contenedor_prueba()
    llm.encolar('{"ambigua": true, "pregunta": "¿Cuál?", "opciones": [{"texto": "A", "valor": "Aa"}]}')
    out = asyncio.run(
        aclaracion_si_procede(
            contenedor.settings, llm, "¿it?",
            [{"role": "user", "content": "hola"}], 0.0, Traza(modo="fijo"), [],
        )
    )
    assert out is not None
    assert "clarify" not in out
    assert out["content"] == "No dispongo de esa documentación"


def test_ambigua_devuelve_chips() -> None:
    contenedor, _, llm, _ = contenedor_prueba()
    llm.encolar(
        '{"ambigua": true, "pregunta": "¿Hardware o software?", '
        '"opciones": [{"texto": "Hardware", "valor": "Es un problema de hardware"}, '
        '{"texto": "Software", "valor": "Es un problema de software"}]}'
    )
    out = asyncio.run(
        aclaracion_si_procede(
            contenedor.settings, llm, "¿falla el equipo?", [], 0.0, Traza(modo="fijo"), [],
        )
    )
    assert out is not None
    assert out["content"] == "¿Hardware o software?"
    assert [o["texto"] for o in out["clarify"]["opciones"]] == ["Hardware", "Software"]
    assert out["sources"] == []


def test_no_ambigua_mantiene_abstencion() -> None:
    contenedor, _, llm, _ = contenedor_prueba()
    llm.encolar('{"ambigua": false, "pregunta": "", "opciones": []}')
    out = asyncio.run(
        aclaracion_si_procede(
            contenedor.settings, llm, "¿hola?", [], 0.0, Traza(modo="fijo"), []
        )
    )
    assert out is not None
    assert out["content"] == "No dispongo de esa documentación"
    assert "clarify" not in out


def test_toggle_desactivado_no_llama_llm() -> None:
    contenedor, _, llm, _ = contenedor_prueba(enable_aclaracion=False)
    out = asyncio.run(
        aclaracion_si_procede(
            contenedor.settings, llm, "¿it?", [], 0.0, Traza(modo="fijo"), []
        )
    )
    assert out is not None
    assert "clarify" not in out
    assert not llm.peticiones
