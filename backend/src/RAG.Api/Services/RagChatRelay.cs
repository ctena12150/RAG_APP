using System.Text.Json.Nodes;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Services;

public sealed record RelayOptions(
    RagChatRequest Request,
    Guid? ConversacionId,
    string? TituloConversacion);

public sealed class RagChatRelay(IRagService rag, IMessageStore messages, IConversationStore conversations)
{
    public async Task RelayAsync(HttpResponse response, RelayOptions options, CancellationToken requestAborted)
    {
        var assistantMessageId = Guid.NewGuid();
        await SseWriter.WriteEventAsync(response, "meta", new
        {
            messageId = assistantMessageId,
            conversationId = options.ConversacionId
        }, requestAborted);

        string? contenidoFinal = null;
        List<SourceCard>? fuentes = null;
        JsonNode? traza = null;
        JsonNode? metricas = null;

        await foreach (var sse in rag.StreamChatAsync(options.Request, requestAborted))
        {
            switch (sse.Evento)
            {
                case "progress":
                    await SseWriter.WriteRawAsync(response, "progress", sse.Data, requestAborted);
                    break;

                case "agent":
                    // progreso del Director en vivo (planificación/herramientas): solo se reenvía
                    await SseWriter.WriteRawAsync(response, "agent", sse.Data, requestAborted);
                    break;

                case "token":
                    await SseWriter.WriteRawAsync(response, "token", sse.Data, requestAborted);
                    break;

                case "done":
                    (contenidoFinal, fuentes, traza, metricas) = ParseDone(sse.Data);
                    if (options.ConversacionId.HasValue && contenidoFinal is not null)
                        await messages.AddAsync(new Message
                        {
                            Id = assistantMessageId,
                            ConversacionId = options.ConversacionId.Value,
                            Rol = "assistant",
                            Contenido = contenidoFinal,
                            FuentesJson = SerializeSources(fuentes),
                            TrazaJson = traza?.ToJsonString(),
                            MetricasJson = metricas?.ToJsonString(),
                            CreadoUtc = DateTime.UtcNow
                        }, requestAborted);
                    await SseWriter.WriteEventAsync(response, "done", new
                    {
                        messageId = assistantMessageId,
                        content = contenidoFinal,
                        sources = fuentes,
                        trace = traza,
                        metrics = metricas
                    }, requestAborted);
                    break;

                case "verified":
                    if (options.ConversacionId.HasValue)
                        await ApplyVerificationAsync(assistantMessageId, sse.Data, requestAborted);
                    await SseWriter.WriteRawAsync(response, "verified", sse.Data, requestAborted);
                    break;

                case "revision_available":
                    if (options.ConversacionId.HasValue)
                        await ApplyRevisionAvailableAsync(assistantMessageId, sse.Data, requestAborted);
                    await SseWriter.WriteRawAsync(response, "revision_available", sse.Data, requestAborted);
                    break;

                case "error":
                    await SseWriter.WriteRawAsync(response, "error", sse.Data, requestAborted);
                    return;
            }
        }

        if (contenidoFinal is null)
        {
            await SseWriter.WriteEventAsync(response, "error", new
            {
                code = "rag_sin_respuesta",
                message = "El servicio RAG terminó sin producir respuesta."
            }, requestAborted);
        }
        else if (options.ConversacionId.HasValue)
        {
            await conversations.TouchAsync(options.ConversacionId.Value, requestAborted);
        }
    }

    private async Task ApplyVerificationAsync(Guid messageId, string dataJson, CancellationToken ct)
    {
        var node = SseParser.Parse(dataJson);
        var verdict = node?["verdict"]?.ToString() ?? "unknown";
        var revision = node?["revision"]?.ToString();
        await messages.ApplyVerificationAsync(messageId, dataJson, verdict == "unsupported" ? revision : null, ct);
    }

    private async Task ApplyRevisionAvailableAsync(Guid messageId, string dataJson, CancellationToken ct)
    {
        var node = SseParser.Parse(dataJson);
        var revision = node?["revision"]?.ToString();
        if (!string.IsNullOrWhiteSpace(revision))
            await messages.ApplyVerificationAsync(messageId, dataJson, revision, ct);
    }

    private static (string? Contenido, List<SourceCard> Fuentes, JsonNode? Traza, JsonNode? Metricas) ParseDone(string dataJson)
    {
        var node = SseParser.Parse(dataJson);
        var content = node?["content"]?.ToString();
        var fuentes = new List<SourceCard>();
        if (node?["sources"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
            {
                fuentes.Add(new SourceCard(
                    item["indice"]?.GetValue<int>() ?? 0,
                    Guid.TryParse(item["documentoId"]?.ToString(), out var docId) ? docId : Guid.Empty,
                    item["documentoNombre"]?.ToString() ?? "",
                    Guid.TryParse(item["chunkId"]?.ToString(), out var chunkId) ? chunkId : Guid.Empty,
                    item["chunkIndice"]?.GetValue<int>() ?? 0,
                    item["pagina"]?.GetValue<int?>(),
                    item["seccion"]?.ToString(),
                    item["fragmento"]?.ToString() ?? "",
                    item["puntuacion"]?.GetValue<double>() ?? 0,
                    item["usada"]?.GetValue<bool>() ?? false));
            }
        }
        return (content, fuentes, node?["trace"], node?["metrics"]);
    }

    private static string? SerializeSources(List<SourceCard>? fuentes) =>
        fuentes is null || fuentes.Count == 0 ? null : RagJson.Serialize(fuentes);
}
