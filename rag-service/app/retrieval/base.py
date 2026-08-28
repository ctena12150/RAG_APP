"""Protocolo estructural que cumplen los almacenes RAG (Postgres y memoria).

Permite tipar el contenedor y el motor sin acoplarlos a ninguna implementación:
cualquier clase con estos métodos es un RagStore válido (duck typing con static typing).
"""

from __future__ import annotations

from typing import Protocol

from app.models import Chunk, Hit


class RagStore(Protocol):
    async def conectar(self) -> None: ...

    async def cerrar(self) -> None: ...

    async def ensure_schema(self) -> None: ...

    async def reemplazar_chunks(
        self,
        documento_id: str,
        dominio: str,
        chunks: list[Chunk],
        embeddings: list[list[float]],
    ) -> list[str]: ...

    async def borrar_documento(self, documento_id: str) -> None: ...

    async def busqueda_vector(
        self,
        embedding: list[float],
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]: ...

    async def busqueda_keyword(
        self,
        termino: str,
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]: ...

    async def contar_documentos_listos(self, dominios: list[str] | None = None) -> int: ...

    async def metadatos_documentos(self) -> list[dict]: ...
