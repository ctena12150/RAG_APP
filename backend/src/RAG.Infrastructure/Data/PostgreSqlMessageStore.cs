using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlMessageStore(IDbConnectionFactory factory) : IMessageStore
{
    private const string SelectMessage = """
        SELECT
            id AS "Id",
            conversacion_id AS "ConversacionId",
            rol AS "Rol",
            contenido AS "Contenido",
            fuentes_json AS "FuentesJson",
            traza_json AS "TrazaJson",
            verificacion_json AS "VerificacionJson",
            metricas_json AS "MetricasJson",
            revision_contenido AS "RevisionContenido",
            creado_utc AS "CreadoUtc"
        FROM app.mensajes
        """;

    public async Task<Message> AddAsync(Message m, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.mensajes (id, conversacion_id, rol, contenido, fuentes_json, traza_json, verificacion_json, metricas_json, revision_contenido, creado_utc)
            VALUES (
                @Id,
                @ConversacionId,
                @Rol,
                @Contenido,
                CAST(@FuentesJson AS jsonb),
                CAST(@TrazaJson AS jsonb),
                CAST(@VerificacionJson AS jsonb),
                CAST(@MetricasJson AS jsonb),
                @RevisionContenido,
                @CreadoUtc
            )
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, m, cancellationToken: ct));
        return m;
    }

    public async Task<Message?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = SelectMessage + " WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Message>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Message>> ListByConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        var sql = SelectMessage + " WHERE conversacion_id = @conversationId ORDER BY creado_utc, id";
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<Message>(new CommandDefinition(sql, new { conversationId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task ApplyVerificationAsync(Guid id, string verificacionJson, string? revisionContenido, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.mensajes
            SET verificacion_json = CAST(@verificacionJson AS jsonb),
                revision_contenido = COALESCE(@revisionContenido, revision_contenido)
            WHERE id = @id
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, verificacionJson, revisionContenido }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Mensaje {id} no existe.");
    }

    public async Task ApplyRevisionAsync(Guid id, string revisionContenido, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.mensajes
            SET contenido = @revisionContenido, revision_contenido = NULL
            WHERE id = @id
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, revisionContenido }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Mensaje {id} no existe.");
    }
}
