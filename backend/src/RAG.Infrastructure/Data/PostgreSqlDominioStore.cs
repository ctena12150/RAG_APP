using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlDominioStore(IDbConnectionFactory factory) : IDominioStore
{
    private const string Select = """
        SELECT
            clave AS "Clave",
            etiqueta AS "Etiqueta",
            descripcion AS "Descripcion",
            ejemplos AS "Ejemplos",
            creado_utc AS "CreadoUtc"
        FROM app.dominios
        """;

    public async Task<IReadOnlyList<Dominio>> ListarAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Dominio>(new CommandDefinition(Select + " ORDER BY creado_utc", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Dominio?> ObtenerPorClaveAsync(string clave, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Dominio>(
            new CommandDefinition(Select + " WHERE clave = @clave", new { clave = clave.Trim().ToLowerInvariant() }, cancellationToken: ct));
    }

    public async Task<Dominio> CrearAsync(Dominio dominio, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.dominios (clave, etiqueta, descripcion, ejemplos, creado_utc)
            VALUES (@Clave, @Etiqueta, @Descripcion, @Ejemplos, @CreadoUtc)
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, dominio, cancellationToken: ct));
        return dominio;
    }

    public async Task ActualizarAsync(Dominio dominio, CancellationToken ct = default)
    {
        const string sql = "UPDATE app.dominios SET etiqueta = @Etiqueta, descripcion = @Descripcion, ejemplos = @Ejemplos WHERE clave = @Clave";
        await using var conn = await factory.OpenAsync(ct);
        if (await conn.ExecuteAsync(new CommandDefinition(sql, dominio, cancellationToken: ct)) == 0)
            throw new KeyNotFoundException($"Dominio {dominio.Clave} no existe.");
    }

    public async Task<bool> EliminarAsync(string clave, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM app.dominios WHERE clave = @clave",
            new { clave = clave.Trim().ToLowerInvariant() }, cancellationToken: ct)) > 0;
    }
}
