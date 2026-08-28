"""Fábricas de la aplicación con inyección de dependencias (testeable sin red)."""

from __future__ import annotations

from dataclasses import dataclass

from app.config import Settings
from app.core.cache import CacheSemantico
from app.core.embeddings import EmbeddingsClient
from app.core.llms import LlmClient
from app.retrieval.base import RagStore
from app.retrieval.engine import RetrievalEngine


@dataclass
class Contenedor:
    settings: Settings
    store: RagStore
    embeddings: EmbeddingsClient
    llm: LlmClient
    engine: RetrievalEngine
    cache: CacheSemantico | None = None


def crear_cache(s: Settings) -> CacheSemantico | None:
    """Caché semántica según configuración (compartida por producción y dobles de prueba)."""
    if not s.enable_cache_consultas:
        return None
    return CacheSemantico(
        umbral_similitud=s.cache_umbral_similitud,
        max_entradas=s.cache_max_entradas,
        ttl_segundos=s.cache_ttl_minutos * 60,
    )


def construir_contenedor(settings: Settings | None = None) -> Contenedor:
    from app.config import get_settings

    s = settings or get_settings()
    if s.rag_store == "memory":
        from app.retrieval.memory_store import MemoryRagStore

        store = MemoryRagStore()
    else:
        from app.retrieval.store import PostgresRagStore

        store = PostgresRagStore(s.database_dsn, s.embedding_dim)
    embeddings = EmbeddingsClient(s)
    llm = LlmClient(s)
    engine = RetrievalEngine(s, store, embeddings, llm)
    return Contenedor(settings=s, store=store, embeddings=embeddings, llm=llm, engine=engine, cache=crear_cache(s))
