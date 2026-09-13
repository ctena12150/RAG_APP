namespace RAG.Domain.Models;

/// <summary>
/// Credencial local (usuario + contraseña) para acceder al chat sin proveedor OIDC.
/// La contraseña viaja siempre como hash BCrypt (<see cref="PasswordHash"/>); nunca en claro.
/// A diferencia de la lista blanca, el match es el nombre de usuario exacto y no pasa por dominios.
/// </summary>
public sealed class UsuarioLocal
{
    public Guid Id { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Nombre { get; set; }
    public string Rol { get; set; } = Roles.Usuario;
    /// <summary>
    /// Dominios que el usuario puede gestionar (subir/borrar documentos y carpetas).
    /// Vacío = todos. Solo aplica al rol teamleader; el superusuario siempre tiene todos.
    /// </summary>
    public string[] Dominios { get; set; } = [];
    public bool Activo { get; set; } = true;
    public DateTime CreadoUtc { get; set; }
}
