using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Endpoints;

public static class DominiosEndpoints
{
    private static readonly Regex ClaveValida = new("^[a-z0-9-]{2,30}$", RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapDominios(this IEndpointRouteBuilder app, bool habilitarAdmin = false)
    {
        var group = app.MapGroup("/api/dominios").WithTags("Dominios");
        group.MapGet("/", ListarAsync);
        var crear = group.MapPost("/", CrearAsync);
        var actualizar = group.MapPatch("/{clave}", ActualizarAsync);
        var eliminar = group.MapDelete("/{clave}", EliminarAsync);
        if (habilitarAdmin)
        {
            crear.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);
            actualizar.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);
            eliminar.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);
        }
        return app;
    }

    private static async Task<Ok<List<Dominio>>> ListarAsync(IDominioStore store, CancellationToken ct)
    {
        var dominios = await store.ListarAsync(ct);
        return TypedResults.Ok(dominios.OrderBy(d => d.CreadoUtc).ToList());
    }

    private static async Task<Results<Created<Dominio>, BadRequest<ControlledException>, Conflict<ControlledException>>> CrearAsync(
        CrearDominioRequest body, IDominioStore store, CancellationToken ct)
    {
        var clave = body.Clave?.Trim().ToLowerInvariant() ?? "";
        if (!ClaveValida.IsMatch(clave))
            throw new ControlledException("clave_invalida", StatusCodes.Status400BadRequest,
                "La clave debe tener 2-30 caracteres minúsculos, números o guiones.");
        if (string.IsNullOrWhiteSpace(body.Etiqueta) || body.Etiqueta.Trim().Length > 100)
            throw new ControlledException("etiqueta_invalida", StatusCodes.Status400BadRequest,
                "La etiqueta es obligatoria (máx. 100 caracteres).");
        if ((body.Descripcion?.Trim().Length ?? 0) > 300)
            throw new ControlledException("descripcion_invalida", StatusCodes.Status400BadRequest,
                "La descripción admite máx. 300 caracteres.");
        var ejemplos = SanearEjemplos(body.Ejemplos);
        if (await store.ObtenerPorClaveAsync(clave, ct) is not null)
            throw new ControlledException("dominio_duplicado", StatusCodes.Status409Conflict,
                $"Ya existe un dominio con clave '{clave}'.");
        var nuevo = new Dominio
        {
            Clave = clave,
            Etiqueta = body.Etiqueta.Trim(),
            Descripcion = body.Descripcion?.Trim() ?? "",
            Ejemplos = ejemplos,
            CreadoUtc = DateTime.UtcNow
        };
        await store.CrearAsync(nuevo, ct);
        return TypedResults.Created($"/api/dominios/{nuevo.Clave}", nuevo);
    }

    private static async Task<Ok<Dominio>> ActualizarAsync(
        string clave, ActualizarDominioRequest body, IDominioStore store, CancellationToken ct)
    {
        var actual = await store.ObtenerPorClaveAsync(clave.Trim().ToLowerInvariant(), ct)
            ?? throw new KeyNotFoundException($"Dominio {clave} no existe.");
        if (body.Etiqueta is null && body.Descripcion is null && body.Ejemplos is null)
            throw new ControlledException("sin_cambios", StatusCodes.Status400BadRequest,
                "Indica etiqueta, descripción o ejemplos a modificar.");
        if (body.Etiqueta is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Etiqueta) || body.Etiqueta.Trim().Length > 100)
                throw new ControlledException("etiqueta_invalida", StatusCodes.Status400BadRequest,
                    "La etiqueta es obligatoria (máx. 100 caracteres).");
            actual.Etiqueta = body.Etiqueta.Trim();
        }
        if (body.Descripcion is not null)
        {
            if (body.Descripcion.Trim().Length > 300)
                throw new ControlledException("descripcion_invalida", StatusCodes.Status400BadRequest,
                    "La descripción admite máx. 300 caracteres.");
            actual.Descripcion = body.Descripcion.Trim();
        }
        if (body.Ejemplos is not null)
            actual.Ejemplos = SanearEjemplos(body.Ejemplos);
        await store.ActualizarAsync(actual, ct);
        return TypedResults.Ok(actual);
    }

    internal static IReadOnlyList<string> SanearEjemplos(IEnumerable<string>? ejemplos)
    {
        if (ejemplos is null) return [];
        var limpios = ejemplos
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .ToList();
        if (limpios.Count > 8)
            throw new ControlledException("ejemplos_invalidos", StatusCodes.Status400BadRequest,
                "Máximo 8 ejemplos por dominio.");
        if (limpios.Any(e => e.Length > 200))
            throw new ControlledException("ejemplos_invalidos", StatusCodes.Status400BadRequest,
                "Cada ejemplo admite máx. 200 caracteres.");
        return limpios;
    }

    private static async Task<NoContent> EliminarAsync(
        string clave, IDominioStore dominios, IDocumentStore documents, IFolderStore folders,
        IUsuarioLocalStore usuarios, CancellationToken ct)
    {
        var normalizada = clave.Trim().ToLowerInvariant();
        _ = await dominios.ObtenerPorClaveAsync(normalizada, ct)
            ?? throw new KeyNotFoundException($"Dominio {clave} no existe.");
        if ((await documents.ListAsync(normalizada, limite: 1, ct: ct)).Count > 0
            || (await folders.ListAsync(normalizada, ct)).Count > 0
            || (await usuarios.ListarAsync(ct)).Any(u => u.Dominios.Contains(normalizada)))
            throw new ControlledException("dominio_en_uso", StatusCodes.Status409Conflict,
                $"El dominio '{normalizada}' tiene documentos, carpetas o usuarios asignados.");
        await dominios.EliminarAsync(normalizada, ct);
        return TypedResults.NoContent();
    }
}

public sealed record CrearDominioRequest(string? Clave, string? Etiqueta, string? Descripcion, IReadOnlyList<string>? Ejemplos = null);
public sealed record ActualizarDominioRequest(string? Etiqueta, string? Descripcion, IReadOnlyList<string>? Ejemplos = null);
