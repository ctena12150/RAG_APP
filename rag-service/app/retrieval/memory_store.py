"""Almacén RAG en memoria: walking skeleton y pruebas sin infraestructura.

Mismos contratos que PostgresRagStore; similitud coseno y scoring keyword en Python puro.
"""

from __future__ import annotations

import math
import re
import threading
from uuid import uuid4

from app.models import Chunk, Hit

_TOKEN_RE = re.compile(r"[\s,.;:!?()\"'\u00bf\u00a1]+")


def _tokens(texto: str) -> list[str]:
    return [t for t in _TOKEN_RE.split(texto.lower()) if len(t) > 2]


class MemoryRagStore:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._chunks: list[dict] = []
        self._documentos: dict[str, dict] = {}

    async def conectar(self) -> None:  # paridad de API con Postgres
        return None

    async def cerrar(self) -> None:
        return None

    async def ensure_schema(self) -> None:
        return None

    async def upsert_documento(self, documento_id: str, nombre: str, dominio: str, estado: int = 2, paginas: int | None = None) -> None:
        with self._lock:
            self._documentos[documento_id] = {
                "id": documento_id,
                "nombreArchivo": nombre,
                "dominio": dominio,
                "estado": estado,
                "totalPaginas": paginas,
            }

    async def reemplazar_chunks(
        self,
        documento_id: str,
        dominio: str,
        chunks: list[Chunk],
        embeddings: list[list[float]],
    ) -> list[str]:
        ids = []
        with self._lock:
            self._chunks = [c for c in self._chunks if c["documento_id"] != documento_id]
            for chunk, embedding in zip(chunks, embeddings):
                cid = str(uuid4())
                ids.append(cid)
                self._chunks.append(
                    {
                        "id": cid,
                        "documento_id": documento_id,
                        "dominio": dominio,
                        **{k: getattr(chunk, k) for k in ("indice", "texto", "pagina", "seccion")},
                        "embedding": embedding,
                    }
                )
        return ids

    async def borrar_documento(self, documento_id: str) -> None:
        with self._lock:
            self._chunks = [c for c in self._chunks if c["documento_id"] != documento_id]
            self._documentos.pop(documento_id, None)

    @staticmethod
    def _coseno(a: list[float], b: list[float]) -> float:
        if not a or not b or len(a) != len(b):
            return 0.0
        producto = sum(x * y for x, y in zip(a, b))
        norma_a = math.sqrt(sum(x * x for x in a))
        norma_b = math.sqrt(sum(y * y for y in b))
        if norma_a == 0 or norma_b == 0:
            return 0.0
        return producto / (norma_a * norma_b)

    def _filtrados(self, dominios: list[str] | None, documentos_ids: list[str] | None) -> list[dict]:
        resultado = []
        for c in self._chunks:
            doc = self._documentos.get(c["documento_id"])
            if doc is None or doc.get("estado") != 2:
                continue
            if dominios and c["dominio"] not in dominios:
                continue
            if documentos_ids and c["documento_id"] not in documentos_ids:
                continue
            resultado.append((c, doc))
        return resultado  # type: ignore[return-value]

    async def busqueda_vector(
        self,
        embedding: list[float],
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]:
        pares = self._filtrados(dominios, documentos_ids)
        scored = sorted(
            ((c, doc, self._coseno(embedding, c["embedding"])) for c, doc in pares),
            key=lambda t: t[2],
            reverse=True,
        )
        return [self._hit(c, doc, s) for c, doc, s in scored[:k]]

    async def busqueda_keyword(
        self,
        termino: str,
        dominios: list[str] | None,
        documentos_ids: list[str] | None,
        k: int,
    ) -> list[Hit]:
        terminos = _tokens(termino)
        if not terminos:
            return []
        scored = []
        for c, doc in self._filtrados(dominios, documentos_ids):
            tokens_chunk = _tokens(c["texto"])
            score = 0.0
            for t in set(terminos):
                apariciones = tokens_chunk.count(t)
                if apariciones:
                    score += 1 + math.log(apariciones)
                    if c["texto"].lower().startswith(t):
                        score += 0.5
            if score > 0:
                scored.append((c, doc, score))
        scored.sort(key=lambda t: t[2], reverse=True)
        return [self._hit(c, doc, s) for c, doc, s in scored[:k]]

    async def contar_documentos_listos(self, dominios: list[str] | None = None) -> int:
        with self._lock:
            docs = [
                d
                for d in self._documentos.values()
                if d.get("estado") == 2 and (not dominios or d["dominio"] in dominios)
            ]
        return len(docs)

    async def metadatos_documentos(self) -> list[dict]:
        with self._lock:
            docs = [dict(d) for d in self._documentos.values()]
            conteo: dict[str, int] = {}
            for c in self._chunks:
                conteo[c["documento_id"]] = conteo.get(c["documento_id"], 0) + 1
        for d in docs:
            d["chunks"] = conteo.get(d["id"], 0)
            d["estado"] = {0: "pendiente", 1: "procesando", 2: "listo", 3: "error"}.get(d["estado"], "desconocido")
        return docs

    @staticmethod
    def _hit(chunk: dict, doc: dict, score: float) -> Hit:
        return Hit(
            chunk_id=chunk["id"],
            documento_id=chunk["documento_id"],
            documento_nombre=doc["nombreArchivo"],
            dominio=chunk["dominio"],
            indice=chunk["indice"],
            texto=chunk["texto"],
            pagina=chunk.get("pagina"),
            seccion=chunk.get("seccion"),
            puntuacion=score,
        )
