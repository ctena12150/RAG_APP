using System.Text.Json;
using System.Text.Json.Nodes;

namespace RAG.Api.Services;

public static class SseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteEventAsync(HttpResponse response, string eventName, object payload, CancellationToken ct)
    {
        var data = payload is string s ? s : JsonSerializer.Serialize(payload, Json);
        await response.WriteAsync($"event: {eventName}\n", ct);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteRawAsync(HttpResponse response, string eventName, string dataJson, CancellationToken ct)
    {
        await response.WriteAsync($"event: {eventName}\ndata: {dataJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

public static class SseParser
{
    public static JsonNode? Parse(string json) => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
}
