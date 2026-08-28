using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.RagClient;

public sealed class RagHttpClient(HttpClient http) : IRagService
{
    public async Task<IngestResponse> IngestAsync(IngestRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("ingest", request, RagJson.Options, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await MapErrorAsync(response, "ingesta fallida", ct).ConfigureAwait(false);
            var payload = await response.Content.ReadFromJsonAsync<IngestResponse>(RagJson.Options, ct).ConfigureAwait(false);
            return payload ?? throw new RagServiceException("rag_invalid_response", "El servicio RAG devolvió una respuesta de ingesta vacía.");
        }
        catch (RagServiceException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RagServiceException("rag_no_disponible", "El servicio RAG no está disponible.", ex);
        }
    }

    public async IAsyncEnumerable<SseEvent> StreamChatAsync(
        RagChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat")
            {
                Content = JsonContent.Create(request, options: RagJson.Options)
            };
            response = await http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RagServiceException("rag_no_disponible", "El servicio RAG no está disponible.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response) throw await MapErrorAsync(response, "consulta rechazada", ct).ConfigureAwait(false);
        }

        using (response)
        await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string? currentEvent = null;
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0)
                {
                    if (currentEvent is not null) { yield return Flush(currentEvent, ref currentEvent); }
                    continue;
                }
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    currentEvent = line["event:".Length..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var data = line["data:".Length..].Trim();
                    yield return new SseEvent(currentEvent ?? "message", data);
                    currentEvent = null;
                }
            }
        }
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.DeleteAsync($"documents/{documentId}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                throw await MapErrorAsync(response, "limpieza de vectores fallida", ct).ConfigureAwait(false);
        }
        catch (RagServiceException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RagServiceException("rag_no_disponible", "El servicio RAG no está disponible.", ex);
        }
    }

    public async Task InvalidarCacheAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsync("cache/invalidar", null, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await MapErrorAsync(response, "invalidación de caché fallida", ct).ConfigureAwait(false);
        }
        catch (RagServiceException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RagServiceException("rag_no_disponible", "El servicio RAG no está disponible.", ex);
        }
    }

    public async Task<RagServiceHealth> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetAsync("health", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new RagServiceHealth(false, null, null);
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return new RagServiceHealth(true, node?["version"]?.ToString(), node?["configuracion"] ?? node?["config"]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new RagServiceHealth(false, null, null);
        }
    }

    public async Task<JsonNode?> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetAsync("models", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static SseEvent Flush(string eventName, ref string? currentEvent)
    {
        currentEvent = null;
        return new SseEvent(eventName, "{}");
    }

    private static async Task<RagServiceException> MapErrorAsync(HttpResponseMessage response, string contexto, CancellationToken ct)
    {
        string detalle;
        try { detalle = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { detalle = ""; }

        var codigo = response.StatusCode switch
        {
            System.Net.HttpStatusCode.ServiceUnavailable => "rag_proveedor_no_configurado",
            _ => "rag_error"
        };
        var mensajeExtraido = ExtraerMensaje(detalle) ?? $"El servicio RAG reportó un error ({(int)response.StatusCode}).";
        if (mensajeExtraido.Length > 500) mensajeExtraido = mensajeExtraido[..500] + "…";
        return new RagServiceException(codigo, $"{contexto}: {mensajeExtraido}");
    }

    private static string? ExtraerMensaje(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var node = JsonNode.Parse(body);
            var detail = node?["detail"];
            // FastAPI devuelve 422 con un ARRAY de errores de validación cuyo "input" repite el body completo:
            // resumir solo el primer fallo para no arrastrar megabytes al mensaje controlado.
            if (detail is System.Text.Json.Nodes.JsonArray errores)
            {
                var primero = errores.Count > 0 ? errores[0] : null;
                var msg = primero?["msg"]?.ToString();
                var loc = primero?["loc"] is System.Text.Json.Nodes.JsonArray ruta
                    ? string.Join(".", ruta.Select(t => t?.ToString()))
                    : null;
                return msg is null ? null : (loc is null ? $"petición inválida: {msg}" : $"petición inválida ({loc}): {msg}");
            }
            return detail?.ToString() ?? node?["message"]?.ToString() ?? node?["error"]?.ToString();
        }
        catch { return null; }
    }
}
