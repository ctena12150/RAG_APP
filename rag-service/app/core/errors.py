"""Errores controlados del servicio: nunca filtran detalles sensibles del proveedor."""

from __future__ import annotations


class RagError(Exception):
    """Base de los errores controlados del servicio RAG."""

    codigo = "rag_error"

    def __init__(self, mensaje: str) -> None:
        super().__init__(mensaje)
        self.mensaje = mensaje


class SinDocumentosError(RagError):
    codigo = "sin_documentos"

    def __init__(self) -> None:
        super().__init__("Todavía no hay documentos indexados. Sube un documento antes de consultar.")


class ProveedorNoConfiguradoError(RagError):
    codigo = "proveedor_no_configurado"


class ProveedorIndisponibleError(RagError):
    """Todos los proveedores de la cadena fallaron; el mensaje es genérico por seguridad."""

    codigo = "proveedor_indisponible"

    def __init__(self, contexto: str) -> None:
        super().__init__(f"El proveedor de IA no está disponible ({contexto}). Inténtalo de nuevo más tarde.")


def traducir_excepcion_proveedor(exc: Exception, contexto: str) -> RagError:
    """Traduce fallos del proveedor a errores controlados sin filtrar datos sensibles."""
    texto = str(exc).lower()
    if "401" in texto or "api key" in texto or "unauthorized" in texto:
        return ProveedorNoConfiguradoError(f"Credenciales del proveedor inválidas o ausentes ({contexto}).")
    if "429" in texto or "rate limit" in texto or "quota" in texto:
        return ProveedorIndisponibleError(contexto)
    return ProveedorIndisponibleError(contexto)
