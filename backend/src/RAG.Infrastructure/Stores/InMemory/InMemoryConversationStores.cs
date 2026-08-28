using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Conversation> _conversations = [];

    public Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        lock (_lock) _conversations[conversation.Id] = Clone(conversation);
        return Task.FromResult(Clone(conversation));
    }

    public Task<Conversation?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_conversations.TryGetValue(id, out var c) ? Clone(c) : null);
    }

    public Task<IReadOnlyList<Conversation>> ListAsync(string? tituloContiene = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<Conversation> query = _conversations.Values;
            if (!string.IsNullOrWhiteSpace(tituloContiene))
                query = query.Where(c => c.Titulo.Contains(tituloContiene, StringComparison.OrdinalIgnoreCase));
            var result = query.OrderByDescending(c => c.ActualizadoUtc).Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<Conversation>>(result);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) _conversations.Remove(id);
        return Task.CompletedTask;
    }

    public Task SetTituloAsync(Guid id, string titulo, bool automatico, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out var c)) throw new KeyNotFoundException($"Conversación {id} no existe.");
            c.Titulo = titulo;
            c.TituloAutomatico = automatico;
        }
        return Task.CompletedTask;
    }

    public Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out var c)) throw new KeyNotFoundException($"Conversación {id} no existe.");
            c.ActualizadoUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    internal void Clear() { lock (_lock) _conversations.Clear(); }

    private static Conversation Clone(Conversation c) => new()
    {
        Id = c.Id,
        Titulo = c.Titulo,
        TituloAutomatico = c.TituloAutomatico,
            Dominios = c.Dominios is { Count: > 0 } ? [.. c.Dominios] : [],
            DocumentosIds = c.DocumentosIds is { Count: > 0 } ? [.. c.DocumentosIds] : null,
        CreadoUtc = c.CreadoUtc,
        ActualizadoUtc = c.ActualizadoUtc
    };
}

public sealed class InMemoryMessageStore : IMessageStore
{
    private readonly object _lock = new();
    private readonly List<Message> _messages = [];

    public Task<Message> AddAsync(Message message, CancellationToken ct = default)
    {
        lock (_lock) _messages.Add(Clone(message));
        return Task.FromResult(Clone(message));
    }

    public Task<Message?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_messages.FirstOrDefault(m => m.Id == id) is { } m ? Clone(m) : null);
    }

    public Task<IReadOnlyList<Message>> ListByConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var result = _messages.Where(m => m.ConversacionId == conversationId)
                .OrderBy(m => m.CreadoUtc).ThenBy(m => m.Id)
                .Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<Message>>(result);
        }
    }

    public Task ApplyVerificationAsync(Guid id, string verificacionJson, string? revisionContenido, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id)
                ?? throw new KeyNotFoundException($"Mensaje {id} no existe.");
            message.VerificacionJson = verificacionJson;
            message.RevisionContenido = revisionContenido;
        }
        return Task.CompletedTask;
    }

    public Task ApplyRevisionAsync(Guid id, string revisionContenido, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id)
                ?? throw new KeyNotFoundException($"Mensaje {id} no existe.");
            message.Contenido = revisionContenido;
            message.RevisionContenido = null;
        }
        return Task.CompletedTask;
    }

    internal void Clear() { lock (_lock) _messages.Clear(); }

    private static Message Clone(Message m) => new()
    {
        Id = m.Id, ConversacionId = m.ConversacionId, Rol = m.Rol, Contenido = m.Contenido,
        FuentesJson = m.FuentesJson, TrazaJson = m.TrazaJson, VerificacionJson = m.VerificacionJson,
        MetricasJson = m.MetricasJson,
        RevisionContenido = m.RevisionContenido, CreadoUtc = m.CreadoUtc
    };
}
