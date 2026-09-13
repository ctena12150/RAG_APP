namespace RAG.Domain.Models;

/// <summary>
/// Roles de acceso al chat. El usuario solo consulta y conserva su historial; el teamleader
/// además sube y gestiona documentos; el superusuario añade la administración de usuarios locales.
/// </summary>
public static class Roles
{
    public const string Usuario = "usuario";
    public const string TeamLeader = "teamleader";
    public const string SuperUsuario = "superusuario";

    public static readonly IReadOnlyList<string> Todos = [Usuario, TeamLeader, SuperUsuario];

    /// <summary>Roles con permiso para subir y gestionar documentos/carpetas.</summary>
    public static readonly IReadOnlyList<string> GestionDocumentos = [TeamLeader, SuperUsuario];

    public static bool EsValido(string? rol) =>
        !string.IsNullOrWhiteSpace(rol) && Todos.Contains(rol.Trim().ToLowerInvariant());

    /// <summary>True si el rol permite gestionar documentos/carpetas.</summary>
    public static bool PuedeGestionarDocumentos(string? rol) =>
        rol is not null && GestionDocumentos.Contains(rol.Trim().ToLowerInvariant());
}
