using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

/// <summary>
/// Credenciales locales (usuario/contraseña) para acceder al chat. El login local NO pasa
/// por la lista blanca: una cuenta creada y activa aquí es suficiente. Incluye la
/// administración de usuarios (rol, alta, activación) que usa el superusuario.
/// </summary>
public interface IUsuarioLocalStore
{
    /// <summary>Devuelve el usuario activo cuyo nombre de usuario coincida (normalizado), o null.</summary>
    Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default);

    /// <summary>Como <see cref="ObtenerPorUsuarioAsync(string, CancellationToken)"/>, pero permite incluir inactivos.</summary>
    Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, bool incluirInactivos, CancellationToken ct = default);

    /// <summary>Devuelve el usuario por id, o null si no existe.</summary>
    Task<UsuarioLocal?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lista completa de usuarios locales (activos e inactivos), ordenada por nombre de usuario.</summary>
    Task<IReadOnlyList<UsuarioLocal>> ListarAsync(CancellationToken ct = default);

    /// <summary>Da de alta un usuario local. La contraseña viaja ya como hash BCrypt.</summary>
    Task<UsuarioLocal> CrearAsync(UsuarioLocal usuario, CancellationToken ct = default);

    /// <summary>
    /// Actualiza los campos editables de un usuario local (rol, activación, nombre, email,
    /// contraseña como hash y dominios). Null = mantener el valor actual.
    /// Lanza <see cref="KeyNotFoundException"/> si el usuario no existe.
    /// </summary>
    Task ActualizarAsync(UsuarioLocal usuario, CancellationToken ct = default);

    /// <summary>
    /// Elimina un usuario local. Lanza <see cref="KeyNotFoundException"/> si no existe.
    /// </summary>
    Task EliminarAsync(Guid id, CancellationToken ct = default);
}
