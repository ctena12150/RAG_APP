"""Modelos compartidos del servicio RAG."""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(slots=True)
class Segmento:
    page: int | None
    text: str


@dataclass(slots=True)
class Chunk:
    indice: int
    texto: str
    pagina: int | None = None
    seccion: str | None = None


@dataclass(slots=True)
class Hit:
    """Fragmento recuperado por cualquier modalidad de búsqueda."""

    chunk_id: str
    documento_id: str
    documento_nombre: str
    dominio: str
    indice: int
    texto: str
    pagina: int | None
    seccion: str | None
    puntuacion: float

    def to_dict(self) -> dict:
        return {
            "chunkId": self.chunk_id,
            "documentoId": self.documento_id,
            "documentoNombre": self.documento_nombre,
            "dominio": self.dominio,
            "indice": self.indice,
            "texto": self.texto,
            "pagina": self.pagina,
            "seccion": self.seccion,
        }

    @classmethod
    def from_dict(cls, datos: dict) -> "Hit":
        """Reconstruye un Hit desde su forma de wire (camelCase), tolerando falta de puntuación."""
        return cls(
            chunk_id=str(datos.get("chunkId", "")),
            documento_id=str(datos.get("documentoId", "")),
            documento_nombre=str(datos.get("documentoNombre", "")),
            dominio=str(datos.get("dominio", "")),
            indice=int(datos.get("indice", 0)),
            texto=str(datos.get("texto", "")),
            pagina=datos.get("pagina"),
            seccion=datos.get("seccion"),
            puntuacion=float(datos.get("puntuacion", 0.0)),
        )


@dataclass(slots=True)
class FuenteCard:
    """Cita estructurada que viaja al frontend."""

    indice: int
    documento_id: str
    documento_nombre: str
    chunk_id: str
    chunk_indice: int
    pagina: int | None
    seccion: str | None
    fragmento: str
    puntuacion: float
    usada: bool = False

    def to_dict(self) -> dict:
        return {
            "indice": self.indice,
            "documentoId": self.documento_id,
            "documentoNombre": self.documento_nombre,
            "chunkId": self.chunk_id,
            "chunkIndice": self.chunk_indice,
            "pagina": self.pagina,
            "seccion": self.seccion,
            "fragmento": self.fragmento[:400],
            "puntuacion": round(self.puntuacion, 4),
            "usada": self.usada,
        }


@dataclass(slots=True)
class Traza:
    """Trazabilidad etapa a etapa del pipeline o del agente."""

    modo: str = "fijo"
    etapas: list[dict] = field(default_factory=list)

    def agregar(self, etapa: str, duracion_ms: int = 0, **detalle: object) -> None:
        entrada: dict = {"etapa": etapa, "duracionMs": duracion_ms}
        if detalle:
            entrada["detalle"] = detalle
        self.etapas.append(entrada)

    def to_dict(self) -> dict:
        return {"modo": self.modo, "etapas": self.etapas}
