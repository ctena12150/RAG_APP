using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Endpoints;

/// <summary>
/// Administración de usuarios locales (solo superusuario): listado, alta con rol y dominios,
/// edición (rol, activación, nombre, email, contraseña, dominios) y borrado con protecciones.
/// La contraseña se guarda siempre como hash BCrypt. Los dominios solo aplican al teamleader
/// y limitan la gestión de documentos/carpetas; vacíos = todos los dominios.
/// </summary>
public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuarios(this IEndpointRouteBuilder app, bool habilitarAdmin = false)
    {
        var group = app.MapGroup("/api/usuarios").WithTags("Usuarios");

        group.MapGet("/", ListarAsync);
        group.MapPost("/", CrearAsync);
        group.MapPatch("/{id:guid}", ActualizarAsync);
        group.MapDelete("/{id:guid}", EliminarAsync);

        if (habilitarAdmin) group.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);

        return app;
    }

    private static async Task<Ok<List<UsuarioDto>>> ListarAsync(IUsuarioLocalStore store, CancellationToken ct)
    {
        var usuarios = await store.ListarAsync(ct);
        return TypedResults.Ok(usuarios.Select(UsuarioDto.From).ToList());
    }

    private static async Task<Results<Created<UsuarioDto>, BadRequest<ControlledException>, Conflict<ControlledException>>> CrearAsync(
        CrearUsuarioRequest body, IUsuarioLocalStore store, CancellationToken ct)
    {
        var usuario = body.Usuario?.Trim();
        var contrasena = body.Contrasena;
        if (string.IsNullOrWhiteSpace(usuario) || usuario.Length > 100)
            throw new ControlledException("usuario_invalido", StatusCodes.Status400BadRequest,
                "El nombre de usuario es obligatorio (máx. 100 caracteres).");
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < 8)
            throw new ControlledException("contrasena_corta", StatusCodes.Status400BadRequest,
                "La contraseña debe tener al menos 8 caracteres.");

        var rol = string.IsNullOrWhiteSpace(body.Rol) ? Roles.Usuario : body.Rol.Trim().ToLowerInvariant();
        if (!Roles.EsValido(rol))
            throw new ControlledException("rol_invalido", StatusCodes.Status400BadRequest,
                $"El rol '{rol}' no es válido. Valores permitidos: {string.Join(", ", Roles.Todos)}.");

        if (await store.ObtenerPorUsuarioAsync(usuario, incluirInactivos: true, ct) is not null)
            throw new ControlledException("usuario_duplicado", StatusCodes.Status409Conflict,
                $"Ya existe un usuario llamado '{usuario}'.");

        var dominios = ValidarDominios(body.Dominios);
        if (rol == Roles.TeamLeader && dominios.Count == 0)
            dominios = [.. Dominios.Todos];

        var nuevo = await store.CrearAsync(new UsuarioLocal
        {
            Id = Guid.NewGuid(),
            Usuario = usuario,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(contrasena),
            Email = NormalizarOpcional(body.Email),
            Nombre = NormalizarOpcional(body.Nombre),
            Rol = rol,
            Dominios = [.. dominios],
            Activo = true,
            CreadoUtc = DateTime.UtcNow
        }, ct);

        return TypedResults.Created($"/api/usuarios/{nuevo.Id}", UsuarioDto.From(nuevo));
    }

    private static async Task<Ok<UsuarioDto>> ActualizarAsync(
        Guid id, ActualizarUsuarioRequest body, IUsuarioLocalStore store, CancellationToken ct)
    {
        var actual = await store.ObtenerPorIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Usuario {id} no existe.");

        if (body.Rol is null && body.Activo is null && body.Email is null && body.Nombre is null
            && body.Contrasena is null && body.Dominios is null)
            throw new ControlledException("sin_cambios", StatusCodes.Status400BadRequest,
                "Indica al menos un campo a modificar.");

        var rol = actual.Rol;
        if (body.Rol is not null)
        {
            rol = body.Rol.Trim().ToLowerInvariant();
            if (!Roles.EsValido(rol))
                throw new ControlledException("rol_invalido", StatusCodes.Status400BadRequest,
                    $"El rol '{body.Rol}' no es válido. Valores permitidos: {string.Join(", ", Roles.Todos)}.");
        }

        var hash = actual.PasswordHash;
        if (body.Contrasena is not null)
        {
            if (body.Contrasena.Length < 8)
                throw new ControlledException("contrasena_corta", StatusCodes.Status400BadRequest,
                    "La contraseña debe tener al menos 8 caracteres.");
            hash = BCrypt.Net.BCrypt.HashPassword(body.Contrasena);
        }

        var dominios = actual.Dominios;
        if (body.Dominios is not null)
            dominios = [.. ValidarDominios(body.Dominios)];
        if (rol == Roles.TeamLeader && dominios.Length == 0 && body.Dominios is null && actual.Rol != Roles.TeamLeader)
            dominios = [.. Dominios.Todos];
        if (rol != Roles.TeamLeader)
            dominios = [];

        actual.Rol = rol;
        actual.PasswordHash = hash;
        actual.Dominios = dominios;
        actual.Activo = body.Activo ?? actual.Activo;
        if (body.Email is not null) actual.Email = NormalizarOpcional(body.Email);
        if (body.Nombre is not null) actual.Nombre = NormalizarOpcional(body.Nombre);

        await store.ActualizarAsync(actual, ct);
        return TypedResults.Ok(UsuarioDto.From(actual));
    }

    private static async Task<NoContent> EliminarAsync(
        Guid id, ClaimsPrincipal user, IUsuarioLocalStore store, CancellationToken ct)
    {
        var actual = await store.ObtenerPorIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Usuario {id} no existe.");

        var identidadPropia = user.FindFirst(AuthRegistration.ClaimEmail)?.Value;
        var yo = identidadPropia is not null
            ? (actual.Email ?? actual.Usuario).Trim().ToLowerInvariant()
            : null;
        if (yo is not null && string.Equals(yo, identidadPropia!.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new ControlledException("usuario_propio", StatusCodes.Status409Conflict,
                "No puedes eliminar tu propio usuario.");

        if (actual.Rol == Roles.SuperUsuario && actual.Activo)
        {
            var otrosSuper = (await store.ListarAsync(ct))
                .Any(u => u.Id != id && u.Rol == Roles.SuperUsuario && u.Activo);
            if (!otrosSuper)
                throw new ControlledException("ultimo_superusuario", StatusCodes.Status409Conflict,
                    "No puedes eliminar el último superusuario activo.");
        }

        await store.EliminarAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static IReadOnlyList<string> ValidarDominios(IReadOnlyList<string>? dominios)
    {
        if (dominios is not { Count: > 0 }) return [];
        var normalizados = dominios.Select(d => d.Trim().ToLowerInvariant()).Distinct().ToList();
        foreach (var d in normalizados)
            if (!Dominios.EsValido(d))
                throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                    $"Dominio '{d}' no válido. Valores permitidos: {string.Join(", ", Dominios.Todos)}.");
        return normalizados;
    }

    private static string? NormalizarOpcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}

public sealed record CrearUsuarioRequest(string? Usuario, string? Contrasena, string? Rol, string? Email, string? Nombre, IReadOnlyList<string>? Dominios);
public sealed record ActualizarUsuarioRequest(string? Rol, bool? Activo, string? Email, string? Nombre, string? Contrasena, IReadOnlyList<string>? Dominios);

public sealed record UsuarioDto(
    Guid Id,
    string Usuario,
    string? Email,
    string? Nombre,
    string Rol,
    string[] Dominios,
    bool Activo,
    DateTime CreadoUtc)
{
    public static UsuarioDto From(UsuarioLocal u) => new(u.Id, u.Usuario, u.Email, u.Nombre, u.Rol, u.Dominios, u.Activo, u.CreadoUtc);
}
