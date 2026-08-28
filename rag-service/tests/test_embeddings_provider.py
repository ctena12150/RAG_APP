"""Selección del proveedor de embeddings activo (sin red)."""

import pytest

from app.config import Settings
from app.core.embeddings import EmbeddingsClient
from app.core.errors import ProveedorNoConfiguradoError


def _cliente(**overrides) -> EmbeddingsClient:
    return EmbeddingsClient(Settings(rag_store="memory", **overrides))


def test_default_ollama_bge_m3():
    assert _cliente()._activo() == ("ollama", "bge-m3")


def test_google_sin_clave_se_salta_y_cae_a_ollama():
    cliente = _cliente(embeddings_chain="google:text-embedding-004,ollama:bge-m3")
    assert cliente._activo() == ("ollama", "bge-m3")


def test_google_con_clave_y_primero_gana():
    cliente = _cliente(
        embeddings_chain="google:text-embedding-004,ollama:bge-m3",
        google_api_key="AIza-fake",
    )
    assert cliente._activo() == ("google", "text-embedding-004")


def test_proveedor_desconocido_se_salta_sin_swap_silencioso():
    # jina ya no existe como proveedor; aunque alguien lo ponga en la cadena no se elige
    cliente = _cliente(embeddings_chain="jina:jina-embeddings-v3,ollama:bge-m3")
    assert cliente._activo() == ("ollama", "bge-m3")


def test_cadena_vacia_o_inusable_lanza_error_controlado():
    with pytest.raises(ProveedorNoConfiguradoError):
        _cliente(embeddings_chain="google:text-embedding-004")._activo()  # sin API key
