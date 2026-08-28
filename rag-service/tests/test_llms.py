"""Pruebas del streaming y fallback del cliente LLM."""

from __future__ import annotations

import pytest

from app.config import Settings
from app.core.errors import RagError
from app.core.llms import LlmClient


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
    def __init__(self, respuesta: _RespuestaStream) -> None:
        self._respuesta = respuesta

    async def __aenter__(self) -> _ClienteHttp:
        return self

    async def __aexit__(self, *_args: object) -> None:
        return None

    def stream(self, *_args: object, **_kwargs: object) -> _RespuestaStream:
        return self._respuesta


@pytest.mark.asyncio
async def test_stream_no_mezcla_fallback_despues_de_tokens(monkeypatch: pytest.MonkeyPatch) -> None:
    respuestas = iter(
        [
            _RespuestaStream(
                ['data: {"choices":[{"delta":{"content":"parcial"}}]}'],
                RuntimeError("conexión interrumpida"),
            ),
        ]
    )

    def crear_cliente(*_args: object, **_kwargs: object) -> _ClienteHttp:
        return _ClienteHttp(next(respuestas))

    monkeypatch.setattr("app.core.llms.httpx.AsyncClient", crear_cliente)
    settings = Settings(
        rag_store="memory",
        ollama_base_url="http://ollama.test/v1",
        groq_api_key="",
        mistral_api_key="",
    )
    cliente = LlmClient(settings)

    resultado: list[str] = []
    with pytest.raises(RagError):
        async for fragmento in cliente.stream(
            [("ollama", "modelo-1"), ("ollama", "modelo-2")],
            [{"role": "user", "content": "pregunta"}],
        ):
            resultado.append(fragmento)

    assert resultado == ["parcial"]
