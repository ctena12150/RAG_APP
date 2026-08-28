using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Document> _documents = [];

    public Task<Document> CreateAsync(Document document, CancellationToken ct = default)
    {
        lock (_lock) _documents[document.Id] = Clone(document);
        return Task.FromResult(Clone(document));
    }

    public Task<Document?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_documents.TryGetValue(id, out var doc) ? Clone(doc) : null);
        }
    }

    public Task<Document?> FindByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var found = _documents.Values.FirstOrDefault(d => d.ContentHash == contentHash);
            return Task.FromResult(found is null ? null : Clone(found));
        }
    }

    public Task UpdateEstadoAsync(Guid id, DocumentStatus estado, string? errorMensaje = null, int? totalPaginas = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_documents.TryGetValue(id, out var doc)) throw new KeyNotFoundException($"Documento {id} no existe.");
            doc.Estado = estado;
            doc.ErrorMensaje = errorMensaje;
            if (totalPaginas.HasValue) doc.TotalPaginas = totalPaginas;
            if (estado == DocumentStatus.Listo || estado == DocumentStatus.Error) doc.ProcesadoUtc = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Document>> ListAsync(string? dominio = null, Guid? folderId = null, string? nombreContiene = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IEnumerable<Document> query = _documents.Values;
            if (dominio is not null) query = query.Where(d => d.Dominio == dominio);
            if (folderId.HasValue) query = query.Where(d => d.FolderId == folderId.Value);
            if (!string.IsNullOrWhiteSpace(nombreContiene))
                query = query.Where(d => d.NombreArchivo.Contains(nombreContiene, StringComparison.OrdinalIgnoreCase));
            var result = query.OrderByDescending(d => d.CreadoUtc).Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<Document>>(result);
        }
    }

    public Task SetFolderAsync(Guid id, Guid? folderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_documents.TryGetValue(id, out var doc)) throw new KeyNotFoundException($"Documento {id} no existe.");
            doc.FolderId = folderId;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock) _documents.Remove(id);
        return Task.CompletedTask;
    }

    internal void Clear() { lock (_lock) _documents.Clear(); }

    private static Document Clone(Document d) => new()
    {
        Id = d.Id,
        NombreArchivo = d.NombreArchivo,
        Dominio = d.Dominio,
        FolderId = d.FolderId,
        TamanoBytes = d.TamanoBytes,
        ContentHash = d.ContentHash,
        Estado = d.Estado,
        ErrorMensaje = d.ErrorMensaje,
        TotalPaginas = d.TotalPaginas,
        CreadoUtc = d.CreadoUtc,
        ProcesadoUtc = d.ProcesadoUtc
    };
}
