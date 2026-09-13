using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

/// <summary>
/// Credenciales locales en PostgreSQL (tabla app.usuarios). El match de usuario es case-insensitive
/// (se normaliza a minúsculas). El hash BCrypt vive en <see cref="UsuarioLocal.PasswordHash"/>.
/// Incluye la administración de usuarios (rol, alta, activación) que usa el superusuario.
/// </summary>
public sealed class PostgreSqlUsuarioLocalStore(IDbConnectionFactory factory) : IUsuarioLocalStore
{
    private const string Columnas = """
        id AS "Id",
        usuario AS "Usuario",
        password_hash AS "PasswordHash",
        email AS "Email",
        nombre AS "Nombre",
        rol AS "Rol",
        dominios AS "Dominios",
        activo AS "Activo",
        creado_utc AS "CreadoUtc"
        """;

    public async Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default) =>
        await ObtenerPorUsuarioAsync(usuario, incluirInactivos: false, ct);

    public async Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, bool incluirInactivos, CancellationToken ct = default)
    {
        // activo solo se filtra cuando NO se piden inactivos; por defecto solo cuentas activas
        var consulta = incluirInactivos
            ? $"""
            SELECT {Columnas}
            FROM app.usuarios
            WHERE lower(usuario) = @usuario
            LIMIT 1
            """
            : $"""
            SELECT {Columnas}
            FROM app.usuarios
            WHERE activo AND lower(usuario) = @usuario
            LIMIT 1
            """;
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioLocal>(
            new CommandDefinition(consulta, new { usuario = Normalizar(usuario) }, cancellationToken: ct));
    }

    public async Task<UsuarioLocal?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Columnas}
            FROM app.usuarios
            WHERE id = @id
            LIMIT 1
            """;
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioLocal>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UsuarioLocal>> ListarAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {Columnas}
            FROM app.usuarios
            ORDER BY lower(usuario)
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<UsuarioLocal>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<UsuarioLocal> CrearAsync(UsuarioLocal usuario, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.usuarios (id, usuario, password_hash, email, nombre, rol, dominios, activo, creado_utc)
            VALUES (@Id, @usuario_nombre, @PasswordHash, @Email, @Nombre, @Rol, @Dominios, true, now())
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            usuario.Id,
            usuario_nombre = usuario.Usuario.Trim(),
            usuario.PasswordHash,
            Email = NormalizarOpcional(usuario.Email),
            Nombre = NormalizarOpcional(usuario.Nombre),
            usuario.Rol,
            usuario.Dominios,
        }, cancellationToken: ct));
        return usuario;
    }

    public async Task ActualizarAsync(UsuarioLocal usuario, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.usuarios SET
                rol = COALESCE(@Rol, rol),
                email = COALESCE(@Email, email),
                nombre = COALESCE(@Nombre, nombre),
                password_hash = COALESCE(@PasswordHash, password_hash),
                dominios = @Dominios,
                activo = COALESCE(@Activo, activo)
            WHERE id = @Id
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            usuario.Id,
            usuario.Rol,
            Email = NormalizarOpcional(usuario.Email),
            Nombre = NormalizarOpcional(usuario.Nombre),
            usuario.PasswordHash,
            usuario.Dominios,
            usuario.Activo,
        }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Usuario {usuario.Id} no existe.");
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM app.usuarios WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Usuario {id} no existe.");
    }

    private static string Normalizar(string usuario) => usuario.Trim().ToLowerInvariant();

    private static string? NormalizarOpcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
