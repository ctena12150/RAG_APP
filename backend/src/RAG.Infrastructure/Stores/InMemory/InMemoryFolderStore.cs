using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

public sealed class InMemoryFolderStore : IFolderStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Folder> _folders = [];

    public Task<Folder> CreateAsync(Folder folder, CancellationToken ct = default)
    {
        lock (_lock) _folders[folder.Id] = Clone(folder);
        return Task.FromResult(Clone(folder));
    }

    public Task<IReadOnlyList<Folder>> ListAsync(string? dominio = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<Folder> query = _folders.Values;
            if (dominio is not null) query = query.Where(f => f.Dominio == dominio);
            var result = query.OrderBy(f => f.Nombre).Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<Folder>>(result);
        }
    }

    public Task<Folder?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_folders.TryGetValue(id, out var folder) ? Clone(folder) : null);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) _folders.Remove(id);
        return Task.CompletedTask;
    }

    private static Folder Clone(Folder f) => new() { Id = f.Id, Nombre = f.Nombre, Dominio = f.Dominio, CreadoUtc = f.CreadoUtc };
}
