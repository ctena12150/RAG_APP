using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

/// <summary>
/// Lista blanca de acceso: un email está permitido si coincide exactamente con una
/// entrada de email o si su dominio coincide con una entrada de dominio.
/// </summary>
public interface IUsuarioPermitidoStore
{
    Task<bool> EstaPermitidoAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<UsuarioPermitido>> ListAsync(CancellationToken ct = default);
    Task<UsuarioPermitido> CreateAsync(UsuarioPermitido usuario, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> SetActivoAsync(Guid id, bool activo, CancellationToken ct = default);
}