using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IFolderStore
{
    Task<Folder> CreateAsync(Folder folder, CancellationToken ct = default);
    Task<IReadOnlyList<Folder>> ListAsync(string? dominio = null, CancellationToken ct = default);
    Task<Folder?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
