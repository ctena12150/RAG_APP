using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface IDominioStore
{
    Task<IReadOnlyList<Dominio>> ListarAsync(CancellationToken ct = default);
    Task<Dominio?> ObtenerPorClaveAsync(string clave, CancellationToken ct = default);
    Task<Dominio> CrearAsync(Dominio dominio, CancellationToken ct = default);
    Task ActualizarAsync(Dominio dominio, CancellationToken ct = default);
    Task<bool> EliminarAsync(string clave, CancellationToken ct = default);
}
