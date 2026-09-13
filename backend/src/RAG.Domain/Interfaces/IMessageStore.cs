using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IMessageStore
{
    Task<Message> AddAsync(Message message, CancellationToken ct = default);
    Task<Message?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> ListByConversationAsync(Guid conversationId, CancellationToken ct = default);
    /// <summary>Últimos N mensajes en orden cronológico (para el historial del chat, sin traer todo).</summary>
    Task<IReadOnlyList<Message>> ListRecientesAsync(Guid conversationId, int limite, CancellationToken ct = default);
    Task ApplyVerificationAsync(Guid id, string verificacionJson, string? revisionContenido, CancellationToken ct = default);
    /// <summary>Aplica la revisión y devuelve el contenido actualizado (una sola sentencia).</summary>
    Task<string> ApplyRevisionAsync(Guid id, string revisionContenido, CancellationToken ct = default);
}
