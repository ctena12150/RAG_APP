using System.Text;
using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlDocumentStore(IDbConnectionFactory factory) : IDocumentStore
{
    private const string SelectDocument = """
        SELECT
            id,
            nombre_archivo AS "NombreArchivo",
            dominio,
            folder_id AS "FolderId",
            tamano_bytes AS "TamanoBytes",
            content_hash AS "ContentHash",
            estado,
            error_mensaje AS "ErrorMensaje",
            total_paginas AS "TotalPaginas",
            creado_utc AS "CreadoUtc",
            procesado_utc AS "ProcesadoUtc"
        FROM app.documentos
        """;

    public async Task<Document> CreateAsync(Document d, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.documentos (id, nombre_archivo, dominio, folder_id, tamano_bytes, content_hash, estado, error_mensaje, total_paginas, creado_utc, procesado_utc)
            VALUES (@Id, @NombreArchivo, @Dominio, @FolderId, @TamanoBytes, @ContentHash, @Estado, @ErrorMensaje, @TotalPaginas, @CreadoUtc, @ProcesadoUtc)
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, d, cancellationToken: ct));
        return d;
    }

    public async Task<Document?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = SelectDocument + " WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Document>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<Document?> FindByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        var sql = SelectDocument + " WHERE content_hash = @contentHash LIMIT 1";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Document>(new CommandDefinition(sql, new { contentHash }, cancellationToken: ct));
    }

    public async Task UpdateEstadoAsync(Guid id, DocumentStatus estado, string? errorMensaje = null, int? totalPaginas = null, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.documentos
            SET estado = @estado,
                error_mensaje = @errorMensaje,
                total_paginas = COALESCE(@totalPaginas, total_paginas),
                procesado_utc = CASE WHEN @estado IN (2, 3) THEN NOW() AT TIME ZONE 'UTC' ELSE procesado_utc END
            WHERE id = @id
            """;
        await using var conn = await factory.OpenAsync(ct);
        // error_mensaje es varchar(1000): truncar siempre para que marcar el error no falle a su vez
        if (errorMensaje is { Length: > 1000 } larga) errorMensaje = larga[..997] + "…";
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { id, estado = (int)estado, errorMensaje, totalPaginas }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Documento {id} no existe.");
    }

    public async Task<IReadOnlyList<Document>> ListAsync(string? dominio = null, Guid? folderId = null, string? nombreContiene = null, CancellationToken ct = default)
    {
        var sql = new StringBuilder(SelectDocument + " WHERE 1=1");
        var parameters = new Dictionary<string, object?>();
        if (dominio is not null) { sql.Append(" AND dominio = @dominio"); parameters["dominio"] = dominio; }
        if (folderId.HasValue) { sql.Append(" AND folder_id = @folderId"); parameters["folderId"] = folderId.Value; }
        if (!string.IsNullOrWhiteSpace(nombreContiene))
        {
            sql.Append(" AND nombre_archivo ILIKE @nombre");
            parameters["nombre"] = $"%{nombreContiene}%";
        }
        sql.Append(" ORDER BY creado_utc DESC");

        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Document>(new CommandDefinition(sql.ToString(), parameters, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetFolderAsync(Guid id, Guid? folderId, CancellationToken ct = default)
    {
        const string sql = "UPDATE app.documentos SET folder_id = @folderId WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, folderId }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Documento {id} no existe.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM app.documentos WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
