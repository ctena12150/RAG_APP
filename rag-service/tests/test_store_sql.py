"""Regresión: numeración de placeholders en las búsquedas del store Postgres.

El embedding/término ocupa $1; los filtros de dominio/documentos deben renumerarse
desde $2 o asyncpg intenta encajar un vector donde va un array (CannotCoerceError).
"""

from __future__ import annotations

from typing import Any

from app.retrieval.store import PostgresRagStore, _vector_literal


class _ConexionFalsa:
    def __init__(self) -> None:
        self.sql = ""
        self.args: tuple[Any, ...] = ()

    async def fetch(self, sql: str, *args: Any):
        self.sql, self.args = sql, args
        return []

    async def __aenter__(self):
        return self

    async def __aexit__(self, *exc):
        return False


class _PoolFalso:
    def __init__(self) -> None:
        self.conexion = _ConexionFalsa()

    def acquire(self):
        return self.conexion


def _store_con_pool_falso() -> tuple[PostgresRagStore, _PoolFalso]:
    store = PostgresRagStore("postgresql://falsa", 8)
    pool = _PoolFalso()
    store._pool = pool  # type: ignore[assignment] — el test ejercita el ensamblado SQL
    return store, pool


async def test_busqueda_vector_numera_embedding_como_1_y_filtros_desde_2():
    store, pool = _store_con_pool_falso()
    embedding = [0.1] * 8
    await store.busqueda_vector(embedding, ["rrhh"], None, 6)
    sql = pool.conexion.sql
    assert "<=> $1::vector" in sql
    assert "ANY($2::varchar[])" in sql
    assert "LIMIT $3" in sql
    # el embedding viaja como literal de texto, no como lista cruda
    assert pool.conexion.args == (_vector_literal(embedding), ["rrhh"], 6)


async def test_busqueda_vector_sin_filtros_solo_embedding_y_k():
    store, pool = _store_con_pool_falso()
    embedding = [0.1] * 8
    await store.busqueda_vector(embedding, None, None, 6)
    assert "ANY(" not in pool.conexion.sql
    assert "LIMIT $2" in pool.conexion.sql
    assert pool.conexion.args == (_vector_literal(embedding), 6)


async def test_busqueda_keyword_numera_termino_como_1_y_filtros_desde_2():
    store, pool = _store_con_pool_falso()
    await store.busqueda_keyword("vacaciones", ["rrhh"], ["d-1"], 6)
    sql = pool.conexion.sql
    assert "websearch_to_tsquery('spanish', $1)" in sql
    assert "ANY($2::varchar[])" in sql
    assert "ANY($3::uuid[])" in sql
    assert "LIMIT $4" in sql


def test_filtros_sql_sin_offset_empiezan_en_uno():
    store = PostgresRagStore("postgresql://falsa", 8)
    filtros, params = store._filtros_sql(None, ["d-1"])
    assert "$1::uuid[]" in filtros
    assert params == [["d-1"]]


def test_filtros_sql_vacios_solo_estado_listo():
    store = PostgresRagStore("postgresql://falsa", 8)
    filtros, params = store._filtros_sql(None, None)
    assert filtros == "d.estado = 2"
    assert params == []
