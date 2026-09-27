using Dapper;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.Estadisticas;

namespace RAG.Infrastructure.Data;

public sealed class PostgreSqlEstadisticasStore(IDbConnectionFactory factory) : IEstadisticasStore
{
    public async Task<EstadisticasResumen> ResumenAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        const string sqlConvs = """SELECT id AS "Id", titulo AS "Titulo", titulo_automatico AS "TituloAutomatico", dominios_json AS "DominiosJson", documentos_ids_json AS "DocumentosIdsJson", usuario_id AS "UsuarioId", creado_utc AS "CreadoUtc", actualizado_utc AS "ActualizadoUtc" FROM app.conversaciones""";
        const string sqlMsgs = """
            SELECT id AS "Id", conversacion_id AS "ConversacionId", rol AS "Rol", contenido AS "Contenido",
                fuentes_json AS "FuentesJson", traza_json AS "TrazaJson", verificacion_json AS "VerificacionJson",
                metricas_json AS "MetricasJson", clarify_json AS "ClarifyJson", media_json AS "MediaJson",
                revision_contenido AS "RevisionContenido", voto AS "Voto", votado_utc AS "VotadoUtc",
                creado_utc AS "CreadoUtc"
            FROM app.mensajes WHERE creado_utc >= @desde AND creado_utc <= @hasta
            """;
        await using var conn = await factory.OpenAsync(ct);
        var convRows = await conn.QueryAsync<ConversationRow>(new CommandDefinition(sqlConvs, cancellationToken: ct));
        var convs = convRows.Select(r => r.ToConversation()).ToDictionary(c => c.Id);
        var msgs = (await conn.QueryAsync<Message>(new CommandDefinition(sqlMsgs,
            new { desde = desde ?? DateTime.MinValue, hasta = hasta ?? DateTime.MaxValue }, cancellationToken: ct))).ToList();
        return EstadisticasAgregador.Resumir(
            msgs.Where(m => m.Rol == "user").ToList(),
            msgs.Where(m => m.Rol == "assistant").ToList(), convs);
    }

    private sealed class ConversationRow
    {
        public Guid Id { get; set; }
        public string Titulo { get; set; } = "";
        public bool TituloAutomatico { get; set; }
        public string? DominiosJson { get; set; }
        public string? DocumentosIdsJson { get; set; }
        public string? UsuarioId { get; set; }
        public DateTime CreadoUtc { get; set; }
        public DateTime ActualizadoUtc { get; set; }

        public Conversation ToConversation() => new()
        {
            Id = Id,
            Titulo = Titulo,
            TituloAutomatico = TituloAutomatico,
            Dominios = string.IsNullOrWhiteSpace(DominiosJson) ? [] : RagJson.Deserialize<List<string>>(DominiosJson) ?? [],
            DocumentosIds = string.IsNullOrWhiteSpace(DocumentosIdsJson) ? null : RagJson.Deserialize<List<Guid>>(DocumentosIdsJson),
            UsuarioId = UsuarioId,
            CreadoUtc = CreadoUtc,
            ActualizadoUtc = ActualizadoUtc
        };
    }
}
