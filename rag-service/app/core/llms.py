"""Cliente de chat OpenAI-compatible con cadena de fallback entre modelos/proveedores.

Groq, Mistral y Ollama exponen la misma API /chat/completions; la cadena se recorre
en orden ante cualquier fallo (modelo retirado, 429, 5xx, red). Los errores siempre
se traducen a excepciones controladas.
"""

from __future__ import annotations

import json
import logging
from collections.abc import AsyncIterator, Sequence
from typing import Any

import httpx

from app.config import Settings
from app.core.errors import traducir_excepcion_proveedor

logger = logging.getLogger(__name__)

_BASE_URLS = {"groq": "groq_base_url", "mistral": "mistral_base_url", "ollama": "ollama_base_url"}
_API_KEYS = {"groq": "groq_api_key", "mistral": "mistral_api_key"}


class LlmClient:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self.ultimo_proveedor: str | None = None
        self.ultimo_modelo: str | None = None
        self.ultimo_fallback: bool = False

    def _endpoint(self, proveedor: str) -> tuple[str, dict[str, str]]:
        base_url = getattr(self._settings, _BASE_URLS[proveedor])
        headers = {"Content-Type": "application/json"}
        api_key_attr = _API_KEYS.get(proveedor)
        if api_key_attr:
            key = getattr(self._settings, api_key_attr)
            if not key:
                from app.core.errors import ProveedorNoConfiguradoError

                raise ProveedorNoConfiguradoError(f"Falta la API key del proveedor '{proveedor}'.")
            headers["Authorization"] = f"Bearer {key}"
        return base_url.rstrip("/") + "/chat/completions", headers

    async def complete(
        self,
        cadena: Sequence[tuple[str, str]],
        messages: list[dict[str, Any]],
        *,
        temperature: float = 0.2,
        max_tokens: int | None = None,
        response_json: bool = False,
        reasoning: str | None = None,
    ) -> str:
        """Llamada no-streaming; prueba cada eslabón de la cadena hasta que uno funcione."""
        ultimo_error: Exception | None = None
        for indice, (proveedor, modelo) in enumerate(cadena):
            try:
                url, headers = self._endpoint(proveedor)
                payload: dict[str, Any] = {
                    "model": modelo,
                    "messages": self._mensajes_para_modelo(proveedor, modelo, messages, reasoning or self._settings.razonamiento),
                    "temperature": temperature,
                }
                if max_tokens:
                    payload["max_tokens"] = max_tokens
                if response_json:
                    payload["response_format"] = {"type": "json_object"}
                self._ajustes_razonamiento(payload, proveedor, modelo, reasoning or self._settings.razonamiento)
                async with httpx.AsyncClient(timeout=self._settings.llm_timeout_seconds) as client:
                    resp = await client.post(url, headers=headers, json=payload)
                    resp.raise_for_status()
                    data = resp.json()
                    contenido = data["choices"][0]["message"].get("content") or ""
                    if not contenido.strip():
                        raise RuntimeError("el proveedor devolvió una respuesta vacía")
                    self._marcar_modelo(proveedor, modelo, indice)
                    return contenido
            except Exception as exc:  # noqa: BLE001 — cualquier fallo pasa al siguiente eslabón
                ultimo_error = exc
                logger.warning("Fallo %s:%s (%s); probando siguiente eslabón", proveedor, modelo, type(exc).__name__)
        raise traducir_excepcion_proveedor(ultimo_error or RuntimeError("cadena vacía"), "chat")

    async def stream(
        self,
        cadena: Sequence[tuple[str, str]],
        messages: list[dict[str, Any]],
        *,
        temperature: float = 0.2,
        max_tokens: int | None = None,
        reasoning: str | None = None,
    ) -> AsyncIterator[str]:
        """Streaming SSE del proveedor; cede fragmentos de texto."""
        ultimo_error: Exception | None = None
        for indice, (proveedor, modelo) in enumerate(cadena):
            produjo_contenido = False
            try:
                url, headers = self._endpoint(proveedor)
                payload: dict[str, Any] = {
                    "model": modelo,
                    "messages": self._mensajes_para_modelo(proveedor, modelo, messages, reasoning or self._settings.razonamiento),
                    "temperature": temperature,
                    "stream": True,
                }
                if max_tokens:
                    payload["max_tokens"] = max_tokens
                self._ajustes_razonamiento(payload, proveedor, modelo, reasoning or self._settings.razonamiento)
                async with httpx.AsyncClient(timeout=self._settings.llm_timeout_seconds) as client:
                    async with client.stream("POST", url, headers=headers, json=payload) as resp:
                        resp.raise_for_status()
                        async for linea in resp.aiter_lines():
                            if not linea.startswith("data:"):
                                continue
                            datos = linea[5:].strip()
                            if datos == "[DONE]":
                                break
                            trozo = json.loads(datos)
                            delta = trozo.get("choices", [{}])[0].get("delta", {})
                            contenido = delta.get("content")
                            if contenido:
                                produjo_contenido = True
                                yield contenido
                        if not produjo_contenido:
                            raise RuntimeError("el proveedor devolvió una respuesta vacía")
                        self._marcar_modelo(proveedor, modelo, indice)
                        return
            except Exception as exc:  # noqa: BLE001
                ultimo_error = exc
                logger.warning("Fallo streaming %s:%s (%s)", proveedor, modelo, type(exc).__name__)
                if produjo_contenido:
                    raise traducir_excepcion_proveedor(exc, "chat streaming") from exc
        raise traducir_excepcion_proveedor(ultimo_error or RuntimeError("cadena vacía"), "chat streaming")

    @staticmethod
    def _mensajes_para_modelo(proveedor: str, modelo: str, messages: list[dict[str, Any]], reasoning: str) -> list[dict[str, Any]]:
        """Evita que Qwen3 consuma el límite entero en razonamiento interno."""
        if proveedor != "ollama" or not modelo.lower().startswith("qwen3") or reasoning != "off":
            return messages
        copia = [dict(mensaje) for mensaje in messages]
        if copia and copia[-1].get("role") == "user":
            copia[-1]["content"] = str(copia[-1].get("content", "")) + "\n/no_think"
        return copia

    @staticmethod
    def _ajustes_razonamiento(payload: dict[str, Any], proveedor: str, modelo: str, reasoning: str) -> None:
        if proveedor == "groq" and "gpt-oss" in modelo.lower():
            payload["reasoning_effort"] = "low" if reasoning == "off" else reasoning
        elif proveedor == "ollama" and modelo.lower().startswith("qwen3"):
            payload["think"] = reasoning != "off"

    def _marcar_modelo(self, proveedor: str, modelo: str, indice: int) -> None:
        self.ultimo_proveedor = proveedor
        self.ultimo_modelo = modelo
        self.ultimo_fallback = indice > 0
