using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure;

namespace RAG.Infrastructure.Data;

/// <summary>
/// Lista blanca en PostgreSQL (tabla app.usuarios_permitidos). El match es email exacto
/// O dominio (parte tras la @), siempre que la entrada esté activa.
/// </summary>
public sealed class PostgreSqlUsuarioPermitidoStore(IDbConnectionFactory factory) : IUsuarioPermitidoStore
{
    private const string Select = """
        SELECT
            id AS "Id",
            email AS "Email",
            dominio AS "Dominio",
            activo AS "Activo",
            creado_utc AS "CreadoUtc"
        FROM app.usuarios_permitidos
        """;

    public async Task<bool> EstaPermitidoAsync(string email, CancellationToken ct = default)
    {
        var normalizado = email.Trim().ToLowerInvariant();
        var arroba = normalizado.IndexOf('@');
        var dominio = arroba >= 0 ? normalizado[(arroba + 1)..] : string.Empty;

        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM app.usuarios_permitidos
                WHERE activo AND (email = @email OR dominio = @dominio)
            )
            """;
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { email = normalizado, dominio }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UsuarioPermitido>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<UsuarioPermitido>(new CommandDefinition(Select + " ORDER BY email NULLS LAST, dominio", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<UsuarioPermitido> CreateAsync(UsuarioPermitido u, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.usuarios_permitidos (id, email, dominio, activo, creado_utc)
            VALUES (@Id, @Email, @Dominio, @Activo, @CreadoUtc)
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            u.Id,
            Email = Normalizar(u.Email),
            Dominio = Normalizar(u.Dominio),
            u.Activo,
            u.CreadoUtc
        }, cancellationToken: ct));
        return u;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM app.usuarios_permitidos WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SetActivoAsync(Guid id, bool activo, CancellationToken ct = default)
    {
        const string sql = "UPDATE app.usuarios_permitidos SET activo = @activo WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { id, activo }, cancellationToken: ct)) > 0;
    }

    private static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return valor.Trim().ToLowerInvariant();
    }
}