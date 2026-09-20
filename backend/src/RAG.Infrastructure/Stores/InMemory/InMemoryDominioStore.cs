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
            new Dominio { Clave = "rrhh", Etiqueta = "Recursos Humanos", Descripcion = "Nóminas, vacaciones, beneficios, políticas de personal", Ejemplos = ["¿Cuántos días de vacaciones tengo?", "¿Cuándo se paga la nómina?", "¿Cómo solicito un permiso?"], CreadoUtc = ahora },
            new Dominio { Clave = "mantenimiento", Etiqueta = "Mantenimiento", Descripcion = "Manuales técnicos, procedimientos de equipos, calibraciones", Ejemplos = ["¿Cada cuánto se revisa la caldera?", "¿Qué mantenimiento preventivo tiene la bomba?", "¿Cómo se calibra el sensor de presión?"], CreadoUtc = ahora },
            new Dominio { Clave = "onboarding", Etiqueta = "Onboarding", Descripcion = "Alta de empleados, checklist, formación inicial", Ejemplos = ["¿Qué hago mi primer día?", "¿Dónde está el manual de bienvenida?", "¿Qué formación inicial es obligatoria?"], CreadoUtc = ahora },
            new Dominio { Clave = "it", Etiqueta = "IT", Descripcion = "Sistemas, accesos, incidencias y soporte tecnológico", Ejemplos = ["No puedo acceder a la VPN, ¿qué hago?", "¿Cómo solicito un equipo nuevo?", "¿Cuál es la política de contraseñas?"], CreadoUtc = ahora },
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
        actual.Ejemplos = dominio.Ejemplos;
        return Task.CompletedTask;
    }

    public Task<bool> EliminarAsync(string clave, CancellationToken ct = default) =>
        Task.FromResult(_dominios.RemoveAll(d => d.Clave == clave.Trim().ToLowerInvariant()) > 0);
}
