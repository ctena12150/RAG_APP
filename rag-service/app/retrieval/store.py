"""Almacén RAG sobre PostgreSQL + extensión pgvector (esquema "rag").

El esquema "rag" pertenece a este servicio; lee solo-metadatos de app.documentos
(creada por el backend .NET) para el Director y para validar estado "listo".
La búsqueda keyword usa el índice full-text nativo configurado en español.
"""

from __future__ import annotations

import logging
import re
from typing import Any

import asyncpg

from app.models import Chunk, Hit

logger = logging.getLogger(__name__)

_TOKEN_RE = re.compile(r"[\s,.;:!?()\"']+")


def _vector_literal(embedding: list[float]) -> str:
    return "[" + ",".join(f"{v:.7f}" for v in embedding) + "]"


class PostgresRagStore:
    def __init__(self, dsn: str, dim: int) -> None:
        self._dsn = dsn
        self._dim = dim
        self._pool: asyncpg.Pool | None = None

    async def conectar(self) -> None:
        self._pool = await asyncpg.create_pool(self._dsn, min_size=1, max_size=8)
        await self.ensure_schema()

    async def cerrar(self) -> None:
        if self._pool:
            await self._pool.close()

    async def ensure_schema(self) -> None:
        assert self._pool is not None
        async with self._pool.acquire() as conn:
            await conn.execute("CREATE EXTENSION IF NOT EXISTS vector")
            await conn.execute("CREATE SCHEMA IF NOT EXISTS rag")
            # la dimensión se fija por despliegue (proveedor de embeddings inmutable)
            await conn.execute(
                f"""
                CREATE TABLE IF NOT EXISTS rag.chunks (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    documento_id uuid NOT NULL,
                    dominio varchar(20) NOT NULL,
                    indice int NOT NULL,
                    texto text NOT NULL,
                    pagina int,
                    seccion varchar(400),
                    embedding vector({self._dim}) NOT NULL,
                    contenido_tsv tsvector GENERATED ALWAYS AS (to_tsvector('spanish', texto)) STORED,
                    creado_utc timestamptz DEFAULT now()
                )
                """
            )
            await conn.execute("CREATE INDEX IF NOT EXISTS ix_chunks_doc ON rag.chunks(documento_id)")
            await conn.execute("CREATE INDEX IF NOT EXISTS ix_chunks_tsv ON rag.chunks USING GIN(contenido_tsv)")
            await conn.execute(
                "CREATE INDEX IF NOT EXISTS ix_chunks_embedding ON rag.chunks USING hnsw(embedding vector_cosine_ops)"
            )

    async def reemplazar_chunks(
        self,
        documento_id: str,
        dominio: str,
        chunks: list[Chunk],
        embeddings: list[list[float]],
    ) -> list[str]:
        assert self._pool is not None
        ids: list[str] = []
        async with self._pool.acquire() as conn:
            async with conn.transaction():
                await conn.execute("DELETE FROM rag.chunks WHERE documento_id = $1", documento_id)
                for chunk, embedding in zip(chunks, embeddings):
                    fila = await conn.fetchrow(
                        """
                        INSERT INTO rag.chunks (documento_id, dominio, indice, texto, pagina, seccion, embedding)
                        VALUES ($1::uuid, $2, $3, $4, $5, $6, $7::vector)
                        RETURNING id
                        """,
                        documento_id,
                        dominio,
                        chunk.indice,
                        chunk.texto,
                        chunk.pagina,
                        chunk.seccion,
                        _vector_literal(embedding),
                    )
                    ids.append(str(fila["id"]))
        return ids

    async def borrar_documento(self, documento_id: str) -> None:
        assert self._pool is not None
        await self._pool.execute("DELETE FROM rag.chunks WHERE documento_id = $1::uuid", documento_id)

    def _filtros_sql(
        self,
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        offset: int = 0,
    ) -> tuple[str, list[Any]]:
        """Condiciones WHERE compartidas. Los placeholders se numeran desde offset+1
        para convivir con parámetros previos de la consulta (p. ej. el embedding en
        la búsqueda vectorial ocupa $1)."""
        condiciones = ["d.estado = 2"]
        params: list[Any] = []
        if dominios:
            params.append(dominios)
            condiciones.append(f"c.dominio = ANY(${offset + len(params)}::varchar[])")
        if documentos_ids:
            params.append(documentos_ids)
            condiciones.append(f"c.documento_id = ANY(${offset + len(params)}::uuid[])")
        return " AND ".join(condiciones), params

    async def busqueda_vector(
        self,
        embedding: list[float],
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]:
        assert self._pool is not None
        # offset=1: $1 es el embedding; los filtros se numeran a partir de $2.
        # el embedding viaja como literal "[a,b,…]" porque asyncpg no tiene codec
        # nativo para el tipo vector de pgvector (igual que en reemplazar_chunks)
        filtros, extra = self._filtros_sql(dominios, documentos_ids, offset=1)
        params = [_vector_literal(embedding), *extra, k]
        sql = f"""
            SELECT c.id::text, c.documento_id::text, d.nombre_archivo, c.dominio,
                   c.indice, c.texto, c.pagina, c.seccion,
                   1 - (c.embedding <=> ${1}::vector) AS score
            FROM rag.chunks c
            JOIN app.documentos d ON d.id = c.documento_id
            WHERE {filtros}
            ORDER BY c.embedding <=> $1::vector
            LIMIT ${len(params)}
            """
        async with self._pool.acquire() as conn:
            filas = await conn.fetch(sql, *params)
        return [self._hit(fila) for fila in filas]

    async def busqueda_keyword(
        self,
        termino: str,
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]:
        assert self._pool is not None
        if not termino.strip():
            return []
        # offset=1: $1 es el término; los filtros se numeran a partir de $2
        filtros, extra = self._filtros_sql(dominios, documentos_ids, offset=1)
        params = [termino, *extra, k]
        sql = f"""
            SELECT c.id::text, c.documento_id::text, d.nombre_archivo, c.dominio,
                   c.indice, c.texto, c.pagina, c.seccion,
                   ts_rank(c.contenido_tsv, websearch_to_tsquery('spanish', $1)) AS score
            FROM rag.chunks c
            JOIN app.documentos d ON d.id = c.documento_id
            WHERE {filtros}
              AND c.contenido_tsv @@ websearch_to_tsquery('spanish', $1)
            ORDER BY score DESC
            LIMIT ${len(params)}
            """
        try:
            async with self._pool.acquire() as conn:
                filas = await conn.fetch(sql, *params)
        except asyncpg.PostgresError as exc:
            logger.warning("Búsqueda keyword falló (%s); se continúa solo con la vectorial", exc)
            return []
        return [self._hit(fila) for fila in filas]

    async def contar_documentos_listos(self, dominios: list[str] | None = None) -> int:
        assert self._pool is not None
        if dominios:
            async with self._pool.acquire() as conn:
                return await conn.fetchval(
                    "SELECT count(*) FROM app.documentos WHERE estado = 2 AND dominio = ANY($1::varchar[])",
                    dominios,
                )
        async with self._pool.acquire() as conn:
            return await conn.fetchval("SELECT count(*) FROM app.documentos WHERE estado = 2")

    async def metadatos_documentos(self) -> list[dict]:
        """Solo-metadatos: el Director nunca ve el contenido de los chunks."""
        assert self._pool is not None
        async with self._pool.acquire() as conn:
            filas = await conn.fetch(
                """
                SELECT d.id::text AS id, d.nombre_archivo, d.dominio, d.estado, d.total_paginas,
                       (SELECT count(*) FROM rag.chunks c WHERE c.documento_id = d.id) AS chunks
                FROM app.documentos d ORDER BY d.creado_utc
                """
            )
        estados = {0: "pendiente", 1: "procesando", 2: "listo", 3: "error"}
        return [
            {
                "id": f["id"],
                "nombreArchivo": f["nombre_archivo"],
                "dominio": f["dominio"],
                "estado": estados.get(int(f["estado"]), "desconocido"),
                "totalPaginas": f["total_paginas"],
                "chunks": int(f["chunks"]),
            }
            for f in filas
        ]

    @staticmethod
    def _hit(fila: asyncpg.Record) -> Hit:
        return Hit(
            chunk_id=str(fila["id"]),
            documento_id=str(fila["documento_id"]),
            documento_nombre=fila["nombre_archivo"],
            dominio=fila["dominio"],
            indice=int(fila["indice"]),
            texto=fila["texto"],
            pagina=fila["pagina"],
            seccion=fila["seccion"],
            puntuacion=float(fila["score"]) if fila["score"] is not None else 0.0,
        )
