using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlConversationStore(IDbConnectionFactory factory) : IConversationStore
{
    private const string SelectConversation = """
        SELECT
            id AS "Id",
            titulo AS "Titulo",
            titulo_automatico AS "TituloAutomatico",
            dominios_json AS "DominiosJson",
            documentos_ids_json AS "DocumentosIdsJson",
            creado_utc AS "CreadoUtc",
            actualizado_utc AS "ActualizadoUtc"
        FROM app.conversaciones
        """;

    public async Task<Conversation> CreateAsync(Conversation c, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app.conversaciones (id, titulo, titulo_automatico, dominios_json, documentos_ids_json, creado_utc, actualizado_utc)
            VALUES (
                @Id,
                @Titulo,
                @TituloAutomatico,
                CAST(@DominiosJson AS jsonb),
                CAST(@DocumentosIdsJson AS jsonb),
                @CreadoUtc,
                @ActualizadoUtc
            )
            """;
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            c.Id,
            c.Titulo,
            c.TituloAutomatico,
            DominiosJson = c.Dominios.Count == 0 ? null : RagJson.Serialize(c.Dominios),
            DocumentosIdsJson = c.DocumentosIds is { Count: > 0 } ? RagJson.Serialize(c.DocumentosIds) : null,
            c.CreadoUtc,
            c.ActualizadoUtc
        }, cancellationToken: ct));
        return c;
    }

    public async Task<Conversation?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = SelectConversation + " WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ConversationRow>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row is null ? null : row.ToConversation();
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(string? tituloContiene = null, CancellationToken ct = default)
    {
        var sql = SelectConversation;
        var parameters = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(tituloContiene))
        {
            sql += " WHERE titulo ILIKE @titulo";
            parameters["titulo"] = $"%{tituloContiene}%";
        }
        sql += " ORDER BY actualizado_utc DESC";

        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ConversationRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.Select(r => r.ToConversation()).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // mensajes se borran en cascada
        const string sql = "DELETE FROM app.conversaciones WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task SetTituloAsync(Guid id, string titulo, bool automatico, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.conversaciones SET titulo = @titulo, titulo_automatico = @automatico, actualizado_utc = NOW() AT TIME ZONE 'UTC' WHERE id = @id
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, titulo, automatico }, cancellationToken: ct));
        if (rows == 0) throw new KeyNotFoundException($"Conversación {id} no existe.");
    }

    public async Task TouchAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = "UPDATE app.conversaciones SET actualizado_utc = NOW() AT TIME ZONE 'UTC' WHERE id = @id";
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    private sealed class ConversationRow
    {
        public Guid Id { get; set; }
        public string Titulo { get; set; } = "";
        public bool TituloAutomatico { get; set; }
        public string? DominiosJson { get; set; }
        public string? DocumentosIdsJson { get; set; }
        public DateTime CreadoUtc { get; set; }
        public DateTime ActualizadoUtc { get; set; }

        public Conversation ToConversation() => new()
        {
            Id = Id,
            Titulo = Titulo,
            TituloAutomatico = TituloAutomatico,
            Dominios = string.IsNullOrWhiteSpace(DominiosJson) ? [] : RagJson.Deserialize<List<string>>(DominiosJson) ?? [],
            DocumentosIds = string.IsNullOrWhiteSpace(DocumentosIdsJson) ? null : RagJson.Deserialize<List<Guid>>(DocumentosIdsJson),
            CreadoUtc = CreadoUtc,
            ActualizadoUtc = ActualizadoUtc
        };
    }
}
