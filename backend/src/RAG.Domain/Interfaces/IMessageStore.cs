using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IMessageStore
{
    Task<Message> AddAsync(Message message, CancellationToken ct = default);
    Task<Message?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> ListByConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task ApplyVerificationAsync(Guid id, string verificacionJson, string? revisionContenido, CancellationToken ct = default);
    Task ApplyRevisionAsync(Guid id, string revisionContenido, CancellationToken ct = default);
}
