using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

/// <summary>
/// Credenciales locales (usuario/contraseña) para acceder al chat. El login local NO pasa
/// por la lista blanca: una cuenta creada y activa aquí es suficiente.
/// </summary>
public interface IUsuarioLocalStore
{
    /// <summary>Devuelve el usuario activo cuyo nombre de usuario coincida (normalizado), o null.</summary>
    Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default);
}
