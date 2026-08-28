using System.Text.Json.Nodes;

namespace RAG.Domain.Models;

public sealed record ChatTurn(string Role, string Content);

public sealed record RagChatRequest(
    string Question,
    IReadOnlyList<ChatTurn> History,
    IReadOnlyList<string>? Dominios,
    IReadOnlyList<Guid>? DocumentIds,
    string Mode,
    IReadOnlyDictionary<string, bool>? OverridesRetrieval = null,
    string? Modelo = null,
    string? Razonamiento = null,
    string? Perfil = null);

public sealed record IngestSegment(int? Page, string Text);

public sealed record IngestChunkDto(int Indice, string Texto, int? Pagina, string? Seccion);

public sealed record IngestRequest(
    Guid DocumentoId,
    string NombreArchivo,
    string Dominio,
    IReadOnlyList<IngestSegment> Segmentos);

public sealed record IngestResponse(IReadOnlyList<IngestChunkDto> Chunks);

public sealed record RagServiceHealth(bool Disponible, string? Version, JsonNode? Configuracion);

public sealed record SseEvent(string Evento, string Data);
