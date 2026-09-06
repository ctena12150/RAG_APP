using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

/// <summary>
/// Lista blanca en memoria (dev/tests): acepta una semilla inicial de entradas permitidas.
/// </summary>
public sealed class InMemoryUsuarioPermitidoStore : IUsuarioPermitidoStore
{
    private readonly List<UsuarioPermitido> _usuarios = [];

    public InMemoryUsuarioPermitidoStore(IEnumerable<UsuarioPermitido>? semilla = null)
    {
        if (semilla is not null) _usuarios.AddRange(semilla);
    }

    public Task<bool> EstaPermitidoAsync(string email, CancellationToken ct = default)
    {
        var normalizado = email.Trim().ToLowerInvariant();
        var arroba = normalizado.IndexOf('@');
        var dominio = arroba >= 0 ? normalizado[(arroba + 1)..] : string.Empty;

        return Task.FromResult(_usuarios.Any(u => u.Activo && (
            (u.Email is not null && string.Equals(u.Email, normalizado, StringComparison.Ordinal)) ||
            (u.Dominio is not null && string.Equals(u.Dominio, dominio, StringComparison.Ordinal)))));
    }

    public Task<IReadOnlyList<UsuarioPermitido>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UsuarioPermitido>>(_usuarios.OrderBy(u => u.Email ?? u.Dominio).ToList());

    public Task<UsuarioPermitido> CreateAsync(UsuarioPermitido usuario, CancellationToken ct = default)
    {
        usuario.Email = Normalizar(usuario.Email);
        usuario.Dominio = Normalizar(usuario.Dominio);
        _usuarios.Add(usuario);
        return Task.FromResult(usuario);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_usuarios.RemoveAll(u => u.Id == id) > 0);

    public Task<bool> SetActivoAsync(Guid id, bool activo, CancellationToken ct = default)
    {
        var usuario = _usuarios.FirstOrDefault(u => u.Id == id);
        if (usuario is null) return Task.FromResult(false);
        usuario.Activo = activo;
        return Task.FromResult(true);
    }

    private static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return valor.Trim().ToLowerInvariant();
    }
}