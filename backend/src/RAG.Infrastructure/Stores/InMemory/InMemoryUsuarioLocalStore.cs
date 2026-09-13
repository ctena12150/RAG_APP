using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

/// <summary>
/// Credenciales locales en memoria (dev/tests): acepta una semilla inicial de cuentas
/// y permite la administración de usuarios (alta, rol, activación) como el superusuario.
/// </summary>
public sealed class InMemoryUsuarioLocalStore : IUsuarioLocalStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, UsuarioLocal> _porUsuario = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryUsuarioLocalStore(IEnumerable<UsuarioLocal>? semilla = null)
    {
        if (semilla is null) return;
        foreach (var usuario in semilla)
            _porUsuario[Normalizar(usuario.Usuario)] = Clone(usuario);
    }

    public Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default) =>
        ObtenerPorUsuarioAsync(usuario, incluirInactivos: false, ct);

    public Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, bool incluirInactivos, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var encontrado = _porUsuario.TryGetValue(Normalizar(usuario), out var match) && (incluirInactivos || match.Activo)
                ? Clone(match)
                : null;
            return Task.FromResult(encontrado);
        }
    }

    public Task<UsuarioLocal?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var encontrado = _porUsuario.Values.FirstOrDefault(u => u.Id == id);
            return Task.FromResult(encontrado is null ? null : Clone(encontrado));
        }
    }

    public Task<IReadOnlyList<UsuarioLocal>> ListarAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var lista = _porUsuario.Values.OrderBy(u => u.Usuario, StringComparer.OrdinalIgnoreCase).Select(Clone).ToList();
            return Task.FromResult<IReadOnlyList<UsuarioLocal>>(lista);
        }
    }

    public Task<UsuarioLocal> CrearAsync(UsuarioLocal usuario, CancellationToken ct = default)
    {
        lock (_lock) _porUsuario[Normalizar(usuario.Usuario)] = Clone(usuario);
        return Task.FromResult(Clone(usuario));
    }

    public Task ActualizarAsync(UsuarioLocal usuario, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var clave = _porUsuario.Keys.FirstOrDefault(k => _porUsuario[k].Id == usuario.Id);
            if (clave is null) throw new KeyNotFoundException($"Usuario {usuario.Id} no existe.");
            _porUsuario[clave] = Clone(usuario);
        }
        return Task.CompletedTask;
    }

    public Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var clave = _porUsuario.Keys.FirstOrDefault(k => _porUsuario[k].Id == id);
            if (clave is null) throw new KeyNotFoundException($"Usuario {id} no existe.");
            _porUsuario.Remove(clave);
        }
        return Task.CompletedTask;
    }

    internal void Clear() { lock (_lock) _porUsuario.Clear(); }

    private static string Normalizar(string usuario) => usuario.Trim().ToLowerInvariant();

    private static UsuarioLocal Clone(UsuarioLocal u) => new()
    {
        Id = u.Id,
        Usuario = u.Usuario,
        PasswordHash = u.PasswordHash,
        Email = u.Email,
        Nombre = u.Nombre,
        Rol = u.Rol,
        Dominios = [.. u.Dominios],
        Activo = u.Activo,
        CreadoUtc = u.CreadoUtc
    };
}
