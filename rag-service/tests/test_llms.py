"""Pruebas del streaming y fallback del cliente LLM (con cliente HTTP compartido)."""

from __future__ import annotations

import pytest

from app.config import Settings
from app.core.errors import RagError
from app.core.llms import LlmClient


class _RespuestaPost:
    def __init__(self, contenido: str) -> None:
        self._contenido = contenido

    def raise_for_status(self) -> None:
        return None

    def json(self) -> dict:
        return {"choices": [{"message": {"content": self._contenido}}]}


class _RespuestaStream:
    def __init__(self, lineas: list[str], fallo: Exception | None = None) -> None:
        self._lineas = lineas
        self._fallo = fallo

    async def __aenter__(self) -> _RespuestaStream:
        return self

    async def __aexit__(self, *_args: object) -> None:
        return None

    def raise_for_status(self) -> None:
        return None

    async def aiter_lines(self):
        for linea in self._lineas:
            yield linea
        if self._fallo:
            raise self._fallo


class _ClienteHttp:
    """Doble del cliente compartido: registra posts y sirve un stream encolado."""

    def __init__(self, respuesta: _RespuestaStream, contenido_post: str = "ok") -> None:
        self._respuesta = respuesta
        self._contenido_post = contenido_post
        self.posts = 0
        self.streams = 0

    async def post(self, *_args: object, **_kwargs: object) -> _RespuestaPost:
        self.posts += 1
        return _RespuestaPost(self._contenido_post)

    def stream(self, *_args: object, **_kwargs: object) -> _RespuestaStream:
        self.streams += 1
        return self._respuesta


def _settings() -> Settings:
    return Settings(
        rag_store="memory",
        ollama_base_url="http://ollama.test/v1",
        groq_api_key="",
        mistral_api_key="",
    )


@pytest.mark.asyncio
async def test_stream_no_mezcla_fallback_despues_de_tokens() -> None:
    cliente = LlmClient(_settings())
    cliente._client = _ClienteHttp(  # type: ignore[assignment]
        _RespuestaStream(
            ['data: {"choices":[{"delta":{"content":"parcial"}}]}'],
            RuntimeError("conexión interrumpida"),
        )
    )

    resultado: list[str] = []
    with pytest.raises(RagError):
        async for fragmento in cliente.stream(
            [("ollama", "modelo-1"), ("ollama", "modelo-2")],
            [{"role": "user", "content": "pregunta"}],
        ):
            resultado.append(fragmento)

    assert resultado == ["parcial"]


@pytest.mark.asyncio
async def test_complete_reutiliza_el_cliente_compartido() -> None:
    """Dos complete() seguidos usan el mismo cliente HTTP (una sola conexión)."""
    cliente = LlmClient(_settings())
    falso = _ClienteHttp(_RespuestaStream([]), contenido_post="respuesta")
    cliente._client = falso  # type: ignore[assignment]

    primero = await cliente.complete([("ollama", "m")], [{"role": "user", "content": "a"}])
    segundo = await cliente.complete([("ollama", "m")], [{"role": "user", "content": "b"}])

    assert (primero, segundo) == ("respuesta", "respuesta")
    assert falso.posts == 2
    assert cliente._client is falso
