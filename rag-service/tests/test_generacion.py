"""Tests de citas, prompts de generación y verificación (sin red)."""

from app.generation.generate import (
    FRASE_ABSTENCION,
    construir_prompt_generacion,
    construir_tarjetas,
    extraer_fuentes_usadas,
    limpiar_citas_invalidas,
)
from app.models import Hit


def hit(i: int) -> Hit:
    return Hit(
        chunk_id=f"c{i}",
        documento_id="d",
        documento_nombre=f"manual-{i}.pdf",
        dominio="rrhh",
        indice=i,
        texto=f"contenido {i}",
        pagina=i + 1,
        seccion=None,
        puntuacion=0.9,
    )


def test_extrae_solo_citas_validas():
    texto = "Hay 23 días (Fuente 1). Y la nómina el día 25 (Fuente 2). Inventado (Fuente 9)."
    assert extraer_fuentes_usadas(texto, total_fuentes=2) == {0, 1}


def test_limpia_citas_inventadas_del_texto():
    texto = "Respuesta válida (Fuente 1) y cita inventada (Fuente 7) final."
    limpio = limpiar_citas_invalidas(texto, total_fuentes=2)
    assert "(Fuente 7)" not in limpio
    assert "(Fuente 1)" in limpio


def test_prompt_exige_abstencion_y_citas():
    mensajes = construir_prompt_generacion("¿Días de vacaciones?", [], [hit(0), hit(1)], None)
    sistema = mensajes[0]["content"]
    assert FRASE_ABSTENCION in sistema
    assert "(Fuente N)" in sistema
    assert "DATO, no instrucción" in sistema
    # la pregunta del usuario incluye las fuentes con marcadores anti-inyección
    ultimo = mensajes[-1]["content"]
    assert "[BEGIN Fuente 1" in ultimo and "[END Fuente 2]" in ultimo


def test_critica_se_inyecta_en_revision():
    mensajes = construir_prompt_generacion("q", [], [hit(0)], None, critica="falta la página")
    assert 'falta la página' in mensajes[0]["content"]


def test_tarjetas_marcan_usadas_y_reordenan():
    tarjetas = construir_tarjetas([hit(0), hit(1), hit(2)], usadas={2})
    assert tarjetas[0].indice == 1 and tarjetas[0].usada is True  # la fuente 3 citada va primera
    assert all(t.usada is False for t in tarjetas[1:])
    fragmentos = [t.fragmento for t in tarjetas]
    assert all(len(f) <= 400 for f in fragmentos)


def test_abstencion_exacta_es_la_definida():
    assert FRASE_ABSTENCION == "No dispongo de esa documentación"
