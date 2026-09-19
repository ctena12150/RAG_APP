"""Dominios gestionados en BD: ingesta valida contra el catálogo y el Director crea un tool por dominio."""

from __future__ import annotations

import asyncio

import pytest
from fastapi.testclient import TestClient

from app.agents.director import crear_herramientas
from app.main import crear_app
from tests.doubles import contenedor_prueba, seed_documentos


@pytest.fixture()
def entorno():
    contenedor, _emb, llm, store = contenedor_prueba()
    app = crear_app(contenedor=contenedor)
    with TestClient(app) as client:
        yield client, llm, store


def test_catalogo_incluye_it_por_defecto():
    contenedor, _, _, _ = contenedor_prueba()
    dominios = asyncio.run(contenedor.engine.listar_dominios())
    claves = {d["clave"] for d in dominios}
    assert {"rrhh", "mantenimiento", "onboarding", "it"} <= claves


def test_ingesta_acepta_dominio_it(entorno):
    client, _, _ = entorno
    resp = client.post(
        "/ingest",
        headers={"X-Internal-Key": "test-key"},
        json={
            "documento_id": "33333333-3333-3333-3333-333333333333",
            "nombre_archivo": "guia-it.md",
            "dominio": "it",
            "segmentos": [{"page": None, "text": "# Accesos\nEl alta de VPN tarda un día laborable."}],
        },
    )
    assert resp.status_code == 200
    assert len(resp.json()["chunks"]) >= 1


def test_director_crea_herramienta_por_dominio():
    contenedor, _, _, store = contenedor_prueba()
    asyncio.run(seed_documentos(store, "doc-it-1", "it", ["El alta de VPN tarda un día laborable."]))
    herramientas, _ = asyncio.run(crear_herramientas(contenedor.engine, contenedor.settings, None, None))
    nombres = {getattr(h, "name", "") for h in herramientas}
    assert "buscar_it" in nombres
    assert "listar_documentos" in nombres
