"""Punto de entrada del servicio RAG (FastAPI + lifespan)."""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.api.routes import crear_router
from app.deps import construir_contenedor

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger(__name__)


async def _warmup(contenedor) -> None:
    """Precalienta conexiones: ping a la BD y un embedding dummy para abrir
    sesiones HTTP/TLS hacia el proveedor. Un fallo solo se loguea: el arranque
    nunca depende del warm-up."""
    try:
        listos = await contenedor.store.contar_documentos_listos()
        logger.info("Warm-up BD OK (%d documentos listos)", listos)
    except Exception as exc:  # noqa: BLE001
        logger.warning("Warm-up BD falló (no bloquea): %s", exc)
    try:
        await contenedor.embeddings.embed(["warmup"])
        logger.info("Warm-up embeddings OK")
    except Exception as exc:  # noqa: BLE001
        logger.warning("Warm-up embeddings falló (no bloquea): %s", exc)


def crear_app(settings=None, contenedor=None) -> FastAPI:

    if contenedor is None:
        contenedor = construir_contenedor(settings)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        await contenedor.store.conectar()
        if contenedor.settings.enable_warmup:
            await _warmup(contenedor)
        yield
        await contenedor.store.cerrar()

    app = FastAPI(title="RAG Service", version="1.0.0", lifespan=lifespan)
    app.include_router(crear_router(contenedor))
    app.state.contenedor = contenedor
    return app


app = crear_app()
