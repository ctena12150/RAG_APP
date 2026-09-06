using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

/// <summary>
/// Credenciales locales en memoria (dev/tests): acepta una semilla inicial de cuentas.
/// </summary>
public sealed class InMemoryUsuarioLocalStore : IUsuarioLocalStore
{
    private readonly Dictionary<string, UsuarioLocal> _porUsuario = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryUsuarioLocalStore(IEnumerable<UsuarioLocal>? semilla = null)
    {
        if (semilla is null) return;
        foreach (var usuario in semilla) _porUsuario[usuario.Usuario.Trim().ToLowerInvariant()] = usuario;
    }

    public Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default)
    {
        var normalizado = usuario.Trim().ToLowerInvariant();
        var encontrado = _porUsuario.TryGetValue(normalizado, out var match) && match.Activo ? match : null;
        return Task.FromResult(encontrado);
    }
}
