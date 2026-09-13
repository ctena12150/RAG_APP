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
    public static IEndpointRouteBuilder MapConversations(this IEndpointRouteBuilder app, bool exigirAuth = false)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversaciones");

        if (exigirAuth) group.RequireAuthorization();

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
        CreateConversationRequest body, ClaimsPrincipal user, IConversationStore conversations, CancellationToken ct)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Titulo = string.IsNullOrWhiteSpace(body.Titulo) ? "Nueva conversación" : body.Titulo.Trim(),
            TituloAutomatico = string.IsNullOrWhiteSpace(body.Titulo),
            Dominios = ValidarDominios(body.Dominios) ?? [],
            DocumentosIds = body.DocumentosIds,
            UsuarioId = DueñoActual(user),
            CreadoUtc = DateTime.UtcNow,
            ActualizadoUtc = DateTime.UtcNow
        };
        await conversations.CreateAsync(conversation, ct);
        return TypedResults.Created($"/api/conversations/{conversation.Id}", conversation);
    }

    private static async Task<Ok<List<Conversation>>> ListAsync(
        ClaimsPrincipal user, IConversationStore conversations, string? q, CancellationToken ct)
    {
        var result = await conversations.ListAsync(q, DueñoActual(user), limite: 100, ct: ct);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task<Ok<object>> GetAsync(Guid id, ClaimsPrincipal user, IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        VerificarAcceso(conversation, DueñoActual(user));
        var history = await messages.ListByConversationAsync(id, ct);
        if (!PuedeVerTraza(user)) OcultarTraza(history);
        return TypedResults.Ok<object>(new { conversacion = conversation, mensajes = history });
    }

    private static async Task<NoContent> DeleteAsync(Guid id, ClaimsPrincipal user, IConversationStore conversations, IRagService rag, CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        VerificarAcceso(conversation, DueñoActual(user));
        await conversations.DeleteAsync(id, ct);
        // las respuestas cacheadas quedan huérfanas al borrar la conversación
        try { await rag.InvalidarCacheAsync(ct); }
        catch (RagServiceException) { /* la invalidación de caché no bloquea el borrado */ }
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<Message>>> ListMessagesAsync(Guid id, ClaimsPrincipal user, IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        VerificarAcceso(conversation, DueñoActual(user));
        var result = await messages.ListByConversationAsync(id, ct);
        if (!PuedeVerTraza(user)) OcultarTraza(result);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task AskAsync(
        Guid id,
        AskRequest body,
        ClaimsPrincipal user,
        HttpResponse response,
        IConversationStore conversations,
        IMessageStore messages,
        IDocumentStore documents,
        RagChatRelay relay,
        CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        VerificarAcceso(conversation, DueñoActual(user));

        if (string.IsNullOrWhiteSpace(body.Pregunta))
            throw new ControlledException("pregunta_requerida", StatusCodes.Status400BadRequest, "La pregunta es obligatoria.");
        ValidacionPregunta.Validar(body.Pregunta);

        // rechazo controlado cuando aún no hay documentos indexados
        if (!await documents.HayListosAsync(ct))
            throw new ControlledException("sin_documentos", StatusCodes.Status409Conflict,
                "Todavía no hay documentos indexados. Sube un documento antes de consultar.");

        var dominios = body.Dominios is { Count: > 0 } ? ValidarDominios(body.Dominios) : conversation.Dominios;
        var documentIds = body.DocumentosIds ?? conversation.DocumentosIds;

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

        var history = (await messages.ListRecientesAsync(id, MaxHistoryTurns + 1, ct))
            .Where(m => m.Id != userMessage.Id)
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

        await relay.RelayAsync(response, new RelayOptions(request, id, null, PuedeVerTraza(user)), ct);
    }

    private static async Task<Ok<object>> AcceptRevisionAsync(
        Guid id, Guid messageId, AcceptRevisionRequest body, ClaimsPrincipal user,
        IConversationStore conversations, IMessageStore messages, CancellationToken ct)
    {
        var conversation = await conversations.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Conversación {id} no existe.");
        VerificarAcceso(conversation, DueñoActual(user));
        var message = await messages.FindByIdAsync(messageId, ct)
            ?? throw new KeyNotFoundException($"Mensaje {messageId} no existe.");
        if (message.ConversacionId != id)
            throw new ControlledException("mensaje_ajeno", StatusCodes.Status400BadRequest, "El mensaje no pertenece a la conversación.");
        if (message.RevisionContenido is null && message.Contenido == body.Contenido && !TieneRevision(message.VerificacionJson))
            throw new ControlledException("sin_revision", StatusCodes.Status400BadRequest, "El mensaje no tiene una revisión sugerida pendiente.");

        var revisionContenido = message.RevisionContenido;
        if (string.IsNullOrWhiteSpace(revisionContenido) && !string.IsNullOrWhiteSpace(body.Contenido))
            revisionContenido = body.Contenido;
        if (string.IsNullOrWhiteSpace(revisionContenido))
            throw new ControlledException("sin_revision", StatusCodes.Status400BadRequest, "El mensaje no tiene una revisión sugerida pendiente.");

        var contenido = await messages.ApplyRevisionAsync(messageId, revisionContenido!, ct);
        return TypedResults.Ok<object>(new { messageId, content = contenido });
    }

    private static bool TieneRevision(string? verificacionJson)
    {
        if (string.IsNullOrWhiteSpace(verificacionJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(verificacionJson);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("revision", out var revision)
                && revision.ValueKind != System.Text.Json.JsonValueKind.Null
                && !(revision.ValueKind == System.Text.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(revision.GetString()));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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

    /// <summary>
    /// La traza técnica del pipeline solo la ve el superusuario (o nadie con auth
    /// desactivada, modo dev). El resto recibe los mensajes con TrazaJson a null.
    /// </summary>
    internal static bool PuedeVerTraza(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return true;
        return string.Equals(AuthRegistration.RolDelPrincipal(user), Roles.SuperUsuario, StringComparison.OrdinalIgnoreCase);
    }

    private static void OcultarTraza(IEnumerable<Message> mensajes)
    {
        foreach (var m in mensajes) m.TrazaJson = null;
    }

    /// <summary>
    /// Identidad del dueño de una conversación: el claim rag:email normalizado (email para
    /// Google; email o nombre de usuario para el login local). Null = auth desactivada.
    /// </summary>
    private static string? DueñoActual(ClaimsPrincipal user) =>
        user.FindFirst(AuthRegistration.ClaimEmail)?.Value?.Trim().ToLowerInvariant();

    /// <summary>
    /// Ownership de conversaciones. Con auth activa (hay dueño en la sesión), solo el dueño
    /// accede; las conversaciones previas sin dueño quedan inaccesibles (nunca se filtran a
    /// otros usuarios). Sin auth (dueño null) el historial es compartido y nada se filtra.
    /// </summary>
    private static void VerificarAcceso(Conversation conversation, string? dueño)
    {
        if (dueño is null) return;
        if (conversation.UsuarioId is not null &&
            string.Equals(conversation.UsuarioId, dueño, StringComparison.OrdinalIgnoreCase))
            return;
        throw new KeyNotFoundException($"Conversación {conversation.Id} no existe.");
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
