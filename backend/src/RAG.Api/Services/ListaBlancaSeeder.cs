using RAG.Api.Configuration;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Services;

/// <summary>
/// Siembra inicial de la lista blanca desde configuración (Auth:WhitelistEmails /
/// Auth:WhitelistDomains, separados por coma). Idempotente: no duplica entradas ya
/// existentes. Después del arranque la lista se administra por SQL en app.usuarios_permitidos.
/// </summary>
public static class ListaBlancaSeeder
{
    public static async Task SembrarAsync(IUsuarioPermitidoStore store, AuthOptions auth, CancellationToken ct = default)
    {
        var emails = Parsear(auth.WhitelistEmails);
        var dominios = Parsear(auth.WhitelistDomains);
        if (emails.Count == 0 && dominios.Count == 0) return;

        var existentes = await store.ListAsync(ct);
        var emailsExistentes = existentes.Where(u => u.Email is not null).Select(u => u.Email!.ToLowerInvariant()).ToHashSet();
        var dominiosExistentes = existentes.Where(u => u.Dominio is not null).Select(u => u.Dominio!.ToLowerInvariant()).ToHashSet();
        var ahora = DateTime.UtcNow;

        foreach (var email in emails.Where(e => !emailsExistentes.Contains(e)))
        {
            await store.CreateAsync(new UsuarioPermitido
            {
                Id = Guid.NewGuid(),
                Email = email,
                Activo = true,
                CreadoUtc = ahora
            }, ct);
        }

        foreach (var dominio in dominios.Where(d => !dominiosExistentes.Contains(d)))
        {
            await store.CreateAsync(new UsuarioPermitido
            {
                Id = Guid.NewGuid(),
                Dominio = dominio,
                Activo = true,
                CreadoUtc = ahora
            }, ct);
        }
    }

    private static List<string> Parsear(string? valor) =>
        valor?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToLowerInvariant()).ToList() ?? [];
}