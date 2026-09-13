using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IDocumentStore
{
    Task<Document> CreateAsync(Document document, CancellationToken ct = default);
    Task<Document?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<Document?> FindByContentHashAsync(string contentHash, CancellationToken ct = default);
    Task UpdateEstadoAsync(Guid id, DocumentStatus estado, string? errorMensaje = null, int? totalPaginas = null, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListAsync(string? dominio = null, Guid? folderId = null, string? nombreContiene = null, int? limite = null, CancellationToken ct = default);
    Task SetFolderAsync(Guid id, Guid? folderId, CancellationToken ct = default);
    /// <summary>Desasigna la carpeta de todos sus documentos en una sola sentencia (borrado de carpeta).</summary>
    Task UnassignFolderAsync(Guid folderId, CancellationToken ct = default);
    /// <summary>True si existe al menos un documento listo (EXISTS, sin descargar la tabla).</summary>
    Task<bool> HayListosAsync(CancellationToken ct = default);
    /// <summary>Conteo ligero para /health (listos, totales).</summary>
    Task<(int Listos, int Totales)> ContarAsync(CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
