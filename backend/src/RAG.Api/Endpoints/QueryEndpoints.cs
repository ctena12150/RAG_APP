using Microsoft.AspNetCore.Mvc;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Api.Services;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Endpoints;

public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQuery(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Consulta");

        group.MapPost("/query", QueryAsync);
        group.MapGet("/health", HealthAsync);
        group.MapGet("/models", ModelsAsync);

        return app;
    }

    private static async Task QueryAsync(
        AskStatelessRequest body,
        HttpResponse response,
        IDocumentStore documents,
        RagChatRelay relay,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Pregunta))
            throw new ControlledException("pregunta_requerida", StatusCodes.Status400BadRequest, "La pregunta es obligatoria.");
        ValidacionPregunta.Validar(body.Pregunta);

        var readyDocs = await documents.ListAsync(ct: ct);
        if (readyDocs.All(d => d.Estado != DocumentStatus.Listo))
            throw new ControlledException("sin_documentos", StatusCodes.Status409Conflict,
                "Todavía no hay documentos indexados. Sube un documento antes de consultar.");

        var dominios = ConversationsEndpoints.ValidarDominios(body.Dominios);
        IReadOnlyList<Guid>? documentIds = body.DocumentosIds;
        if (documentIds is { Count: > 0 })
        {
            var validIds = readyDocs.Where(d => d.Estado == DocumentStatus.Listo).Select(d => d.Id).ToHashSet();
            documentIds = documentIds.Where(validIds.Contains).ToList();
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        var request = new RagChatRequest(
            Question: body.Pregunta.Trim(),
            History: [],
            Dominios: dominios is { Count: > 0 } ? dominios : null,
            DocumentIds: documentIds is { Count: > 0 } ? documentIds : null,
            Mode: string.IsNullOrWhiteSpace(body.Mode) ? "auto" : body.Mode.Trim(),
            OverridesRetrieval: body.OverridesRetrieval is { Count: > 0 } ? body.OverridesRetrieval : null,
            Modelo: body.Modelo,
            Razonamiento: body.Razonamiento);

        await relay.RelayAsync(response, new RelayOptions(request, null, null), ct);
    }

    private static async Task<IResult> HealthAsync(
        IDocumentStore documents,
        IRagService rag,
        [FromServices] StorageOptions storage,
        CancellationToken ct)
    {
        RagServiceHealth ragHealth;
        try { ragHealth = await rag.GetHealthAsync(ct); }
        catch (RagServiceException) { ragHealth = new RagServiceHealth(false, null, null); }

        var docs = await documents.ListAsync(ct: ct);

        return Results.Ok(new
        {
            estado = "ok",
            almacenamiento = storage.Provider,
            documentosListos = docs.Count(d => d.Estado == DocumentStatus.Listo),
            documentosTotales = docs.Count,
            ragService = new
            {
                disponible = ragHealth.Disponible,
                version = ragHealth.Version,
                configuracion = ragHealth.Configuracion
            },
            momentoUtc = DateTime.UtcNow
        });
    }

    private static async Task<IResult> ModelsAsync(IRagService rag, CancellationToken ct)
    {
        var modelos = await rag.GetModelsAsync(ct);
        return modelos is null
            ? Results.Ok(new { modelos = Array.Empty<object>() })
            : Results.Ok(modelos);
    }
}

public sealed record AskStatelessRequest(
    string? Pregunta,
    IReadOnlyList<string>? Dominios,
    IReadOnlyList<Guid>? DocumentosIds,
    string? Mode,
    IReadOnlyDictionary<string, bool>? OverridesRetrieval = null,
    string? Modelo = null,
    string? Razonamiento = null);
