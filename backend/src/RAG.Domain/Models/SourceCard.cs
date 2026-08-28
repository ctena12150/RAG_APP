namespace RAG.Domain.Models;

public sealed record SourceCard(
    int Indice,
    Guid DocumentoId,
    string DocumentoNombre,
    Guid ChunkId,
    int ChunkIndice,
    int? Pagina,
    string? Seccion,
    string Fragmento,
    double Puntuacion,
    bool Usada);
