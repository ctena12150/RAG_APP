using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlFolderStore(IDbConnectionFactory factory) : IFolderStore
{
    private const string SelectFolder = """
        SELECT
            id AS "Id",
            nombre AS "Nombre",
            dominio AS "Dominio",
            creado_utc AS "CreadoUtc"
        FROM app.carpetas
        """;

    public async Task<Folder> CreateAsync(Folder f, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.carpetas (id, nombre, dominio, creado_utc)
            VALUES (@Id, @Nombre, @Dominio, @CreadoUtc)
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, f, cancellationToken: ct));
        return f;
    }

    public async Task<IReadOnlyList<Folder>> ListAsync(string? dominio = null, CancellationToken ct = default)
    {
        var sql = SelectFolder;
        var parameters = new Dictionary<string, object?>();
        if (dominio is not null) { sql += " WHERE dominio = @dominio"; parameters["dominio"] = dominio; }
        sql += " ORDER BY nombre";

        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Folder>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Folder?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = SelectFolder + " WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Folder>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // los documentos quedan sin carpeta vía ON DELETE SET NULL
        const string sql = "DELETE FROM app.carpetas WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
