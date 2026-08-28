using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IDocumentStore
{
    Task<Document> CreateAsync(Document document, CancellationToken ct = default);
    Task<Document?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<Document?> FindByContentHashAsync(string contentHash, CancellationToken ct = default);
    Task UpdateEstadoAsync(Guid id, DocumentStatus estado, string? errorMensaje = null, int? totalPaginas = null, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListAsync(string? dominio = null, Guid? folderId = null, string? nombreContiene = null, CancellationToken ct = default);
    Task SetFolderAsync(Guid id, Guid? folderId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
