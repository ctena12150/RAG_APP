using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.Estadisticas;

namespace RAG.Infrastructure.Stores.InMemory;

public sealed class InMemoryEstadisticasStore(IConversationStore conversaciones, IMessageStore mensajes) : IEstadisticasStore
{
    public async Task<EstadisticasResumen> ResumenAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        var convs = (await conversaciones.ListAsync(null, null, null, ct))
            .ToDictionary(c => c.Id);
        var usuarios = new List<Message>();
        var asistentes = new List<Message>();
        foreach (var c in convs.Values)
        {
            foreach (var m in await mensajes.ListByConversationAsync(c.Id, ct))
            {
                if (m.CreadoUtc < (desde ?? DateTime.MinValue) || m.CreadoUtc > (hasta ?? DateTime.MaxValue)) continue;
                if (m.Rol == "user") usuarios.Add(m);
                else if (m.Rol == "assistant") asistentes.Add(m);
            }
        }
        return EstadisticasAgregador.Resumir(usuarios, asistentes, convs);
    }
}
