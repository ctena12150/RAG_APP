using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Api.Services;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Endpoints;

public static class ConversationsEndpoints
{
    private const int MaxHistoryTurns = 12;
    public static IEndpointRouteBuilder MapConversations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversaciones");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapGet("/{id:guid}/messages", ListMessagesAsync);
        group.MapPost("/{id:guid}/messages", AskAsync);
        group.MapPatch("/{id:guid}/messages/{messageId:guid}/revision", AcceptRevisionAsync);

        return app;
    }

    private static async Task<Created<Conversation>> CreateAsync(
        CreateConversationRequest body, IConversationStore conversations, CancellationToken ct)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Titulo = string.IsNullOrWhiteSpace(body.Titulo) ? "Nueva conversación" : body.Titulo.Trim(),
            TituloAutomatico = string.IsNullOrWhiteSpace(body.Titulo),
            Dominios = ValidarDominios(body.Dominios) ?? [],
            DocumentosIds = body.DocumentosIds,
            CreadoUtc = DateTime.UtcNow,
            ActualizadoUtc = DateTime.UtcNow
        };
        await conversations.CreateAsync(conversation, ct);
        return TypedResults.Created($"/api/conversations/{conversation.Id}", conversation);
    }

    private static async Task<Ok<List<Conversation>>> ListAsync(
        IConversationStore conversations, string? q, CancellationToken ct)
    {
        var result = await conversations.ListAsync(q, ct);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task<Ok<object>> GetAsync(Guid id, IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        var history = await messages.ListByConversationAsync(id, ct);
        return TypedResults.Ok<object>(new { conversacion = conversation, mensajes = history });
    }

    private static async Task<NoContent> DeleteAsync(Guid id, IConversationStore conversations, IRagService rag, CancellationToken ct)
    {
        _ = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        await conversations.DeleteAsync(id, ct);
        // las respuestas cacheadas quedan huérfanas al borrar la conversación
        try { await rag.InvalidarCacheAsync(ct); }
        catch (RagServiceException) { /* la invalidación de caché no bloquea el borrado */ }
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<Message>>> ListMessagesAsync(Guid id, IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        _ = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        var result = await messages.ListByConversationAsync(id, ct);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task AskAsync(
        Guid id,
        AskRequest body,
        HttpResponse response,
        IConversationStore conversations,
        IMessageStore messages,
        IDocumentStore documents,
        RagChatRelay relay,
        CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");

        if (string.IsNullOrWhiteSpace(body.Pregunta))
            throw new ControlledException("pregunta_requerida", StatusCodes.Status400BadRequest, "La pregunta es obligatoria.");
        ValidacionPregunta.Validar(body.Pregunta);

        // rechazo controlado cuando aún no hay documentos indexados
        var readyDocs = await documents.ListAsync(ct: ct);
        if (readyDocs.All(d => d.Estado != DocumentStatus.Listo))
            throw new ControlledException("sin_documentos", StatusCodes.Status409Conflict,
                "Todavía no hay documentos indexados. Sube un documento antes de consultar.");

        var dominios = body.Dominios is { Count: > 0 } ? ValidarDominios(body.Dominios) : conversation.Dominios;
        var documentIds = body.DocumentosIds ?? conversation.DocumentosIds;
        if (documentIds is { Count: > 0 })
        {
            var validIds = readyDocs.Where(d => d.Estado == DocumentStatus.Listo).Select(d => d.Id).ToHashSet();
            documentIds = documentIds.Where(validIds.Contains).ToList();
        }

        var userMessage = await messages.AddAsync(new Message
        {
            Id = Guid.NewGuid(),
            ConversacionId = id,
            Rol = "user",
            Contenido = body.Pregunta.Trim(),
            CreadoUtc = DateTime.UtcNow
        }, ct);

        if (conversation.TituloAutomatico)
            await conversations.SetTituloAsync(id, DerivarTitulo(body.Pregunta), automatico: true, ct);

        var history = (await messages.ListByConversationAsync(id, ct))
            .Where(m => m.Id != userMessage.Id)
            .OrderBy(m => m.CreadoUtc)
            .TakeLast(MaxHistoryTurns)
            .Select(m => new ChatTurn(m.Rol, m.Contenido))
            .ToList();

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        var request = new RagChatRequest(
            Question: body.Pregunta.Trim(),
            History: history,
            Dominios: dominios is { Count: > 0 } ? dominios : null,
            DocumentIds: documentIds is { Count: > 0 } ? documentIds : null,
            Mode: string.IsNullOrWhiteSpace(body.Mode) ? "auto" : body.Mode.Trim(),
            OverridesRetrieval: body.OverridesRetrieval is { Count: > 0 } ? body.OverridesRetrieval : null,
            Modelo: body.Modelo,
            Razonamiento: body.Razonamiento,
            Perfil: body.Perfil);

        await relay.RelayAsync(response, new RelayOptions(request, id, null), ct);
    }

    private static async Task<Ok<object>> AcceptRevisionAsync(
        Guid id, Guid messageId, AcceptRevisionRequest body, IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        _ = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        var message = await messages.FindByIdAsync(messageId, ct)
            ?? throw new KeyNotFoundException($"Mensaje {messageId} no existe.");
        if (message.ConversacionId != id)
            throw new ControlledException("mensaje_ajeno", StatusCodes.Status400BadRequest, "El mensaje no pertenece a la conversación.");
        if (!message.VerificacionJson!.Contains("revision", StringComparison.Ordinal) && message.RevisionContenido is null && message.Contenido == body.Contenido)
            throw new ControlledException("sin_revision", StatusCodes.Status400BadRequest, "El mensaje no tiene una revisión sugerida pendiente.");

        var revisionContenido = message.RevisionContenido;
        if (string.IsNullOrWhiteSpace(revisionContenido) && !string.IsNullOrWhiteSpace(body.Contenido))
            revisionContenido = body.Contenido;
        if (string.IsNullOrWhiteSpace(revisionContenido))
            throw new ControlledException("sin_revision", StatusCodes.Status400BadRequest, "El mensaje no tiene una revisión sugerida pendiente.");

        await messages.ApplyRevisionAsync(messageId, revisionContenido!, ct);
        var updated = await messages.FindByIdAsync(messageId, ct);
        return TypedResults.Ok<object>(new { messageId, content = updated?.Contenido });
    }

    internal static IReadOnlyList<string>? ValidarDominios(IReadOnlyList<string>? dominios)
    {
        if (dominios is not { Count: > 0 }) return null;
        foreach (var d in dominios)
            if (!Dominios.EsValido(d))
                throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                    $"Dominio '{d}' no válido. Valores permitidos: {string.Join(", ", Dominios.Todos)}.");
        return [.. dominios.Select(d => d.Trim().ToLowerInvariant())];
    }

    internal static string DerivarTitulo(string pregunta)
    {
        var limpio = pregunta.Trim().ReplaceLineEndings(" ");
        return limpio.Length <= 60 ? limpio : limpio[..57] + "…";
    }
}

public sealed record CreateConversationRequest(string? Titulo, IReadOnlyList<string>? Dominios, IReadOnlyList<Guid>? DocumentosIds);
public sealed record AskRequest(
    string? Pregunta,
    IReadOnlyList<string>? Dominios,
    IReadOnlyList<Guid>? DocumentosIds,
    string? Mode,
    IReadOnlyDictionary<string, bool>? OverridesRetrieval = null,
    string? Modelo = null,
    string? Razonamiento = null,
    string? Perfil = null);
public sealed record AcceptRevisionRequest(string? Contenido);
