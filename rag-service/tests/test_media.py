"""Recursos multimedia de los chunks citados."""

from __future__ import annotations

from app.generation.media import extraer_media
from app.models import Hit


def _hit(texto: str, nombre: str = "Guía de onboarding", pagina: int | None = 4, seccion: str | None = "Primer día") -> Hit:
    return Hit(
        chunk_id="c1", documento_id="d1", documento_nombre=nombre, dominio="onboarding",
        indice=0, texto=texto, pagina=pagina, seccion=seccion, puntuacion=0.9,
    )


def test_video_azure_con_contexto_y_procedencia() -> None:
    hits = [_hit("Ver vídeo de bienvenida https://cuenta.blob.core.windows.net/videos/a1b2c3.mp4?sv=2024&sig=abc")]
    recursos = extraer_media(hits)
    assert len(recursos) == 1
    r = recursos[0]
    assert r["tipo"] == "video"
    assert r["proveedor"] == "azure"
    assert r["url"].endswith(".mp4?sv=2024&sig=abc")
    assert r["contexto"] == "Ver vídeo de bienvenida"
    assert r["documento"] == "Guía de onboarding"
    assert r["pagina"] == 4


def test_dedupe_conserva_primer_contexto() -> None:
    url = "https://x.example/v.mp4"
    hits = [_hit(f"Ver {url}"), _hit(f"Otro {url}")]
    recursos = extraer_media(hits)
    assert len(recursos) == 1
    assert recursos[0]["contexto"] == "Ver"


def test_rechaza_esquemas_peligrosos_y_respeta_cap() -> None:
    hits = [
        _hit("x javascript:alert(1)"),
        _hit("a https://a.example/1.mp4 b https://b.example/2.pdf"),
    ]
    recursos = extraer_media(hits, max_recursos=1)
    assert len(recursos) == 1
    assert recursos[0]["url"].startswith("https://")


def test_sin_urls_devuelve_vacio() -> None:
    assert extraer_media([_hit("texto sin enlaces")]) == []
