"""Cliente de embeddings con lotes de máximo 64 textos, concurrencia limitada y reintento 429.

Los embeddings NO tienen fallback cruzado entre proveedores: los vectores de dos
proveedores viven en espacios distintos y mezclarlos rompería la búsqueda. La cadena
sirve solo para elegir UN proveedor activo: el primer eslabón cuya credencial esté
presente. Cambiar de proveedor exige reindexar (EMBEDDING_DIM + espacio vectorial).
"""

from __future__ import annotations

import asyncio
import logging
from collections.abc import Sequence

import httpx

from app.config import Settings
from app.core.errors import ProveedorIndisponibleError, ProveedorNoConfiguradoError

logger = logging.getLogger(__name__)

_GOOGLE_BASE_URL = "https://generativelanguage.googleapis.com/v1beta/openai"


class EmbeddingsClient:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._limiter = asyncio.Semaphore(max(settings.embedding_max_concurrency, 1))

    def _activo(self) -> tuple[str, str]:
        """Devuelve (proveedor, modelo) del primer eslabón con credencial disponible."""
        for proveedor, modelo in self._settings.chain("embeddings"):
            if proveedor == "ollama":
                return proveedor, modelo  # local/remoto: siempre usable si está en la cadena
            if proveedor == "google" and self._settings.google_api_key:
                return proveedor, modelo
            # proveedores sin soporte o sin credencial se saltan (nunca swap silencioso)
            logger.debug("Proveedor de embeddings '%s' saltado (no usable)", proveedor)
        raise ProveedorNoConfiguradoError(
            "No hay proveedor de embeddings usable. Configura OLLAMA_BASE_URL o GOOGLE_API_KEY."
        )

    async def embed(self, textos: Sequence[str]) -> list[list[float]]:
        """Calcula embeddings respetando el tamaño máximo de lote (≤64) y sin llamadas redundantes."""
        if not textos:
            return []
        proveedor, modelo = self._activo()
        lote_max = max(min(self._settings.embedding_batch_size, 64), 1)
        sublotes = [list(textos[i : i + lote_max]) for i in range(0, len(textos), lote_max)]
        resultados: list[list[list[float]] | None] = [None] * len(sublotes)

        async def procesar(indice: int, lote: list[str]) -> None:
            async with self._limiter:
                resultados[indice] = await self._embed_lote(proveedor, modelo, lote)

        await asyncio.gather(*(procesar(i, lote) for i, lote in enumerate(sublotes)))
        planos: list[list[float]] = []
        for parcial in resultados:
            planos.extend(parcial or [])
        return planos

    async def _embed_lote(self, proveedor: str, modelo: str, lote: list[str]) -> list[list[float]]:
        url, headers = self._url_y_cabeceras(proveedor)
        reintentos = max(self._settings.embedding_max_retries, 1)
        ultimo_error: Exception | None = None
        for intento in range(reintentos):
            try:
                async with httpx.AsyncClient(timeout=60) as client:
                    resp = await client.post(
                        url + "/embeddings",
                        headers=headers,
                        json={"model": modelo, "input": lote},
                    )
                    if resp.status_code == 429:
                        espera = min(2**intento * 0.5, 8)
                        logger.warning("429 de embeddings; reintentando en %.1fs", espera)
                        await asyncio.sleep(espera)
                        continue
                    resp.raise_for_status()
                    data = resp.json()
                    ordenados = sorted(data["data"], key=lambda d: d.get("index", 0))
                    return [item["embedding"] for item in ordenados]
            except Exception as exc:  # noqa: BLE001 — reintento con backoff; error final controlado abajo
                ultimo_error = exc
                logger.warning("Intento %d/%d de embeddings falló (%s)", intento + 1, reintentos, type(exc).__name__)
                await asyncio.sleep(min(2**intento * 0.5, 8))
        raise ProveedorIndisponibleError(f"embeddings ({type(ultimo_error).__name__})")

    def _url_y_cabeceras(self, proveedor: str) -> tuple[str, dict[str, str]]:
        headers = {"Content-Type": "application/json"}
        if proveedor == "google":
            headers["Authorization"] = f"Bearer {self._settings.google_api_key}"
            return _GOOGLE_BASE_URL, headers
        base = self._settings.ollama_base_url.rstrip("/")
        return base, headers
