using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IEstadisticasStore
{
    Task<EstadisticasResumen> ResumenAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default);
}
