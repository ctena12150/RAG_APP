using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IConversationStore
{
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default);
    Task<Conversation?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> ListAsync(string? tituloContiene = null, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SetTituloAsync(Guid id, string titulo, bool automatico, CancellationToken ct = default);
    Task TouchAsync(Guid id, CancellationToken ct = default);
}
