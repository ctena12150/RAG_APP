using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Stores.InMemory;

public sealed class InMemoryDominioStore : IDominioStore
{
    private readonly List<Dominio> _dominios = [];

    public InMemoryDominioStore()
    {
        var ahora = DateTime.UtcNow;
        _dominios.AddRange(
        [
            new Dominio { Clave = "rrhh", Etiqueta = "Recursos Humanos", Descripcion = "Nóminas, vacaciones, beneficios, políticas de personal", CreadoUtc = ahora },
            new Dominio { Clave = "mantenimiento", Etiqueta = "Mantenimiento", Descripcion = "Manuales técnicos, procedimientos de equipos, calibraciones", CreadoUtc = ahora },
            new Dominio { Clave = "onboarding", Etiqueta = "Onboarding", Descripcion = "Alta de empleados, checklist, formación inicial", CreadoUtc = ahora },
            new Dominio { Clave = "it", Etiqueta = "IT", Descripcion = "Sistemas, accesos, incidencias y soporte tecnológico", CreadoUtc = ahora },
        ]);
    }

    public Task<IReadOnlyList<Dominio>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Dominio>>(_dominios.OrderBy(d => d.CreadoUtc).ToList());

    public Task<Dominio?> ObtenerPorClaveAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult(_dominios.FirstOrDefault(d => d.Clave == clave.Trim().ToLowerInvariant()));

    public Task<Dominio> CrearAsync(Dominio dominio, CancellationToken ct = default)
    {
        dominio.Clave = dominio.Clave.Trim().ToLowerInvariant();
        _dominios.Add(dominio);
        return Task.FromResult(dominio);
    }

    public Task ActualizarAsync(Dominio dominio, CancellationToken ct = default)
    {
        var actual = _dominios.FirstOrDefault(d => d.Clave == dominio.Clave);
        if (actual is null) throw new KeyNotFoundException($"Dominio {dominio.Clave} no existe.");
        actual.Etiqueta = dominio.Etiqueta;
        actual.Descripcion = dominio.Descripcion;
        return Task.CompletedTask;
    }

    public Task<bool> EliminarAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult(_dominios.RemoveAll(d => d.Clave == clave.Trim().ToLowerInvariant()) > 0);
}
