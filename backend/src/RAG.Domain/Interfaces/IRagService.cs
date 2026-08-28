using System.Text.Json.Nodes;
using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IRagService
{
    IAsyncEnumerable<SseEvent> StreamChatAsync(RagChatRequest request, CancellationToken ct = default);
    Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task InvalidarCacheAsync(CancellationToken ct = default);
    Task<RagServiceHealth> GetHealthAsync(CancellationToken ct = default);
    Task<JsonNode?> GetModelsAsync(CancellationToken ct = default);
}
