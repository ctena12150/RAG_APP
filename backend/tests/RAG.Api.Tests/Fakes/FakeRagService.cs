using System.Text;
using System.Text.Json.Nodes;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Tests.Fakes;

public sealed class FakeRagService : IRagService
{
    public List<RagChatRequest> ChatRequests { get; } = [];
    public List<IngestRequest> IngestRequests { get; } = [];
    public List<Guid> DeletedDocuments { get; } = [];
    public int InvalidacionesCache { get; private set; }

    /// eventos SSE que se emitirán en la siguiente llamada a StreamChatAsync (en orden)
    public Queue<List<SseEvent>> ScriptedChatResponses { get; } = new();

    public bool FailIngest { get; set; }
    public bool FailDelete { get; set; }

    public Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct = default)
    {
        if (FailIngest)
            throw new RagServiceException("rag_proveedor_no_configurado", "ingesta fallida: proveedor no configurado.");

        IngestRequests.Add(request);

        // chunking simulado determinista: un chunk por segmento
        var chunks = request.Segmentos
            .Select((s, i) => new IngestChunkDto(i, s.Text, s.Page, null))
            .ToList();
        return Task.FromResult(new IngestResponse(chunks));
    }

    public async IAsyncEnumerable<SseEvent> StreamChatAsync(
        RagChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ChatRequests.Add(request);
        var events = ScriptedChatResponses.Count > 0 ? ScriptedChatResponses.Dequeue() : DefaultResponse();
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        if (FailDelete) throw new RagServiceException("rag_error", "limpieza de vectores fallida: error remoto.");
        DeletedDocuments.Add(documentId);
        return Task.CompletedTask;
    }

    public Task InvalidarCacheAsync(CancellationToken ct = default)
    {
        InvalidacionesCache++;
        return Task.CompletedTask;
    }

    public Task<RagServiceHealth> GetHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new RagServiceHealth(true, "fake-1.0", null));

    public Task<JsonNode?> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<JsonNode?>(new JsonObject { ["modelos"] = new JsonArray() });

    private static List<SseEvent> DefaultResponse()
    {
        var done = """
            {"content":"La respuesta final.","sources":[{"indice":1,"documentoId":"11111111-1111-1111-1111-111111111111","documentoNombre":"manual.pdf","chunkId":"22222222-2222-2222-2222-222222222222","chunkIndice":0,"pagina":3,"seccion":null,"fragmento":"Texto de la fuente","puntuacion":0.9,"usada":true}],"trace":{"etapas":[{"etapa":"generacion","duracionMs":12}]}}
            """;
        return
        [
            new SseEvent("token", """{"t":"La resp"}"""),
            new SseEvent("token", """{"t":"uesta final."}"""),
            new SseEvent("done", done),
            new SseEvent("verified", """{"verdict":"supported"}""")
        ];
    }
}

public static class SseText
{
    public static string Encode(string eventName, string dataJson)
    {
        var sb = new StringBuilder();
        sb.Append("event: ").Append(eventName).Append('\n');
        sb.Append("data: ").Append(dataJson).Append("\n\n");
        return sb.ToString();
    }
}
