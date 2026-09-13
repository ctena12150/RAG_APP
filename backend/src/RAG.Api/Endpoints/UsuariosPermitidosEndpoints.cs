using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Endpoints;

/// <summary>
/// Administración de la lista blanca de acceso al chat (login Google): las entradas son
/// emails exactos O dominios (parte tras la @), nunca ambos. El valor de una entrada es
/// inmutable: para corregirlo se borra y se crea otra. Solo accesible al superusuario.
/// </summary>
public static class UsuariosPermitidosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosPermitidos(this IEndpointRouteBuilder app, bool habilitarAdmin = false)
    {
        var group = app.MapGroup("/api/usuarios-permitidos").WithTags("ListaBlanca");

        group.MapGet("/", ListarAsync);
        group.MapPost("/", CrearAsync);
        group.MapPatch("/{id:guid}", ActualizarAsync);
        group.MapDelete("/{id:guid}", EliminarAsync);

        if (habilitarAdmin) group.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);

        return app;
    }

    private static async Task<Ok<List<UsuarioPermitidoDto>>> ListarAsync(IUsuarioPermitidoStore store, CancellationToken ct)
    {
        var entradas = await store.ListAsync(ct);
        return TypedResults.Ok(entradas.Select(UsuarioPermitidoDto.From).ToList());
    }

    private static async Task<Results<Created<UsuarioPermitidoDto>, BadRequest<ControlledException>, Conflict<ControlledException>>> CrearAsync(
        CrearPermitidoRequest body, IUsuarioPermitidoStore store, CancellationToken ct)
    {
        var email = Normalizar(body.Email);
        var dominio = Normalizar(body.Dominio);
        if ((email is null) == (dominio is null))
            throw new ControlledException("entrada_invalida", StatusCodes.Status400BadRequest,
                "Indica exactamente un email o un dominio (nunca ambos).");
        if (email is not null && !email.Contains('@'))
            throw new ControlledException("entrada_invalida", StatusCodes.Status400BadRequest,
                "El email debe contener '@'.");
        if (dominio is not null && dominio.Contains('@'))
            throw new ControlledException("entrada_invalida", StatusCodes.Status400BadRequest,
                "El dominio es la parte tras la '@' (sin '@').");

        var existentes = await store.ListAsync(ct);
        if (existentes.Any(u => (email is not null && u.Email == email) || (dominio is not null && u.Dominio == dominio)))
            throw new ControlledException("entrada_duplicada", StatusCodes.Status409Conflict,
                $"La entrada '{email ?? dominio}' ya está en la lista blanca.");

        var nuevo = await store.CreateAsync(new UsuarioPermitido
        {
            Id = Guid.NewGuid(),
            Email = email,
            Dominio = dominio,
            Activo = true,
            CreadoUtc = DateTime.UtcNow
        }, ct);

        return TypedResults.Created($"/api/usuarios-permitidos/{nuevo.Id}", UsuarioPermitidoDto.From(nuevo));
    }

    private static async Task<Results<Ok<UsuarioPermitidoDto>, BadRequest<ControlledException>>> ActualizarAsync(
        Guid id, ActualizarPermitidoRequest body, IUsuarioPermitidoStore store, CancellationToken ct)
    {
        if (body.Activo is null)
            throw new ControlledException("sin_cambios", StatusCodes.Status400BadRequest,
                "Indica la activación a modificar.");

        if (!await store.SetActivoAsync(id, body.Activo.Value, ct))
            throw new KeyNotFoundException($"Entrada {id} no existe.");

        var match = (await store.ListAsync(ct)).First(u => u.Id == id);
        return TypedResults.Ok(UsuarioPermitidoDto.From(match));
    }

    private static async Task<NoContent> EliminarAsync(Guid id, IUsuarioPermitidoStore store, CancellationToken ct)
    {
        if (!await store.DeleteAsync(id, ct))
            throw new KeyNotFoundException($"Entrada {id} no existe.");
        return TypedResults.NoContent();
    }

    private static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();
}

public sealed record CrearPermitidoRequest(string? Email, string? Dominio);
public sealed record ActualizarPermitidoRequest(bool? Activo);

public sealed record UsuarioPermitidoDto(
    Guid Id,
    string? Email,
    string? Dominio,
    bool Activo,
    DateTime CreadoUtc)
{
    public static UsuarioPermitidoDto From(UsuarioPermitido u) => new(u.Id, u.Email, u.Dominio, u.Activo, u.CreadoUtc);
}
