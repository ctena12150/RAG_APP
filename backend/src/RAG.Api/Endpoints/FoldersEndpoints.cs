using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Endpoints;

public static class FoldersEndpoints
{
    public static IEndpointRouteBuilder MapFolders(this IEndpointRouteBuilder app, bool habilitarRoles = false)
    {
        var group = app.MapGroup("/api/folders").WithTags("Carpetas");

        // crear/borrar carpetas gestiona documentos → rol de equipo cuando hay auth
        group.MapGet("/", ListAsync);
        var crear = group.MapPost("/", CreateAsync);
        var borrar = group.MapDelete("/{id:guid}", DeleteAsync);
        if (habilitarRoles)
        {
            crear.RequireAuthorization(AuthRegistration.PoliticaEquipo);
            borrar.RequireAuthorization(AuthRegistration.PoliticaEquipo);
        }

        return app;
    }

    private static async Task<Ok<List<Folder>>> ListAsync(IFolderStore folders, IDominioStore dominios, string? dominio, CancellationToken ct)
    {
        if (dominio is not null && await dominios.ObtenerPorClaveAsync(dominio.Trim().ToLowerInvariant(), ct) is null)
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest, $"Dominio '{dominio}' no válido.");
        var result = await folders.ListAsync(dominio, ct);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task<Results<Created<Folder>, BadRequest<ControlledException>>> CreateAsync(
        CreateFolderRequest body, ClaimsPrincipal user, IFolderStore folders, IDominioStore dominios, CancellationToken ct)
    {
        var nombre = body.Nombre?.Trim() ?? "";
        if (nombre.Length == 0 || nombre.Length > 100)
            throw new ControlledException("nombre_invalido", StatusCodes.Status400BadRequest,
                "El nombre de carpeta es obligatorio (máx. 100 caracteres).");
        if (await dominios.ObtenerPorClaveAsync(body.Dominio?.Trim().ToLowerInvariant() ?? "", ct) is null)
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                $"Dominio '{body.Dominio}' no válido.");
        AuthRegistration.ExigirDominioGestionado(user, body.Dominio!.Trim().ToLowerInvariant());

        var folder = new Folder { Id = Guid.NewGuid(), Nombre = nombre, Dominio = body.Dominio.Trim().ToLowerInvariant(), CreadoUtc = DateTime.UtcNow };
        await folders.CreateAsync(folder, ct);
        return TypedResults.Created($"/api/folders", folder);
    }

    private static async Task<NoContent> DeleteAsync(Guid id, ClaimsPrincipal user, IFolderStore folders, IDocumentStore documents, CancellationToken ct)
    {
        var folder = await folders.FindByIdAsync(id, ct) ?? throw new KeyNotFoundException($"Carpeta {id} no existe.");
        AuthRegistration.ExigirDominioGestionado(user, folder.Dominio);
        // las carpetas son una capa organizativa: los documentos pasan a "sin categoría"
        await documents.UnassignFolderAsync(id, ct);
        await folders.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }
}

public sealed record CreateFolderRequest(string? Nombre, string? Dominio);
