using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

/// <summary>
/// Credenciales locales en PostgreSQL (tabla app.usuarios). El match de usuario es case-insensitive
/// (se normaliza a minúsculas). El hash BCrypt vive en <see cref="UsuarioLocal.PasswordHash"/>.
/// </summary>
public sealed class PostgreSqlUsuarioLocalStore(IDbConnectionFactory factory) : IUsuarioLocalStore
{
    public async Task<UsuarioLocal?> ObtenerPorUsuarioAsync(string usuario, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id AS "Id",
                usuario AS "Usuario",
                password_hash AS "PasswordHash",
                email AS "Email",
                nombre AS "Nombre",
                activo AS "Activo",
                creado_utc AS "CreadoUtc"
            FROM app.usuarios
            WHERE activo AND lower(usuario) = @usuario
            LIMIT 1
            """;
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UsuarioLocal>(
            new CommandDefinition(sql, new { usuario = Normalizar(usuario) }, cancellationToken: ct));
    }

    private static string Normalizar(string usuario) => usuario.Trim().ToLowerInvariant();
}
