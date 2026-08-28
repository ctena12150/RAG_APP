"""Dobles de prueba: embeddings/LLM falsos deterministas, sin red ni API keys."""

from __future__ import annotations

import math
from collections.abc import AsyncIterator
from typing import Any

from app.config import Settings
from app.deps import Contenedor
from app.models import Chunk
from app.retrieval.engine import RetrievalEngine
from app.retrieval.memory_store import MemoryRagStore


def settings_prueba(**overrides) -> Settings:
    base: dict[str, Any] = dict(
        rag_store="memory",
        enable_query_expansion=False,
        enable_reranking=False,
        enable_self_verification=False,
        internal_api_key="test-key",
        # herméticos: el .env real no debe filtrarse en los tests
        groq_api_key="",
        mistral_api_key="",
        # el Director construye un ChatOpenAI REAL: apuntarlo a un puerto cerrado
        # para que falle al instante y sin red (el fallback al fijo lo cubre)
        ollama_base_url="http://127.0.0.1:9/v1",
        planner_chain="ollama:planificador-no-disponible",
    )
    base.update(overrides)
    return Settings(**base)


class EmbeddingsFalsos:
    """Hashing determinista: textos similares → vectores próximos (bolsa de tokens 64d)."""

    def __init__(self) -> None:
        self.llamadas = 0

    async def embed(self, textos):
        self.llamadas += len(textos)
        return [self._vector(t) for t in textos]

    @staticmethod
    def _vector(texto: str, dim: int = 64) -> list[float]:
        v = [0.0] * dim
        for token in texto.lower().split():
            indice = hash(token) % dim
            v[indice] += 1.0
        norma = math.sqrt(sum(x * x for x in v)) or 1.0
        return [x / norma for x in v]


class LlmFalso:
    """LLM doble: devuelve respuestas encoladas por llamada 'complete' y tokens fijos en stream."""

    def __init__(self) -> None:
        self.respuestas: list[str] = []
        self.peticiones: list[list[dict]] = []

    def encolar(self, *respuestas: str) -> None:
        self.respuestas.extend(respuestas)

    async def complete(self, cadena, messages, **kwargs) -> str:
        self.peticiones.append(messages)
        return self.respuestas.pop(0) if self.respuestas else "Respuesta por defecto."

    async def stream(self, cadena, messages, **kwargs) -> AsyncIterator[str]:
        self.peticiones.append(messages)
        texto = self.respuestas.pop(0) if self.respuestas else "Respuesta por defecto."
        for palabra in texto.split(" "):
            yield palabra + " "
            await __import__("asyncio").sleep(0)


def contenedor_prueba(**overrides) -> tuple[Contenedor, EmbeddingsFalsos, LlmFalso, MemoryRagStore]:
    s = settings_prueba(**overrides)
    store = MemoryRagStore()
    emb = EmbeddingsFalsos()
    llm = LlmFalso()
    engine = RetrievalEngine(s, store, emb, llm)
    from app.deps import crear_cache

    cache = crear_cache(s)
    return Contenedor(settings=s, store=store, embeddings=emb, llm=llm, engine=engine, cache=cache), emb, llm, store


async def seed_documentos(store: MemoryRagStore, documento_id: str, dominio: str, parrafos: list[str], nombre="doc.md") -> None:
    await store.upsert_documento(documento_id, nombre, dominio, estado=2)
    chunks = [
        Chunk(indice=i, texto=p, pagina=None, seccion=None)
        for i, p in enumerate(parrafos)
    ]
    emb = EmbeddingsFalsos()
    vectores = await emb.embed([c.texto for c in chunks])
    await store.reemplazar_chunks(documento_id, dominio, chunks, vectores)
