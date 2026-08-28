using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Endpoints;

public static class FoldersEndpoints
{
    public static IEndpointRouteBuilder MapFolders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/folders").WithTags("Carpetas");

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<Ok<List<Folder>>> ListAsync(IFolderStore folders, string? dominio, CancellationToken ct)
    {
        if (dominio is not null && !Dominios.EsValido(dominio))
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest, $"Dominio '{dominio}' no válido.");
        var result = await folders.ListAsync(dominio, ct);
        return TypedResults.Ok(result.ToList());
    }

    private static async Task<Results<Created<Folder>, BadRequest<ControlledException>>> CreateAsync(
        CreateFolderRequest body, IFolderStore folders, CancellationToken ct)
    {
        var nombre = body.Nombre?.Trim() ?? "";
        if (nombre.Length == 0 || nombre.Length > 100)
            throw new ControlledException("nombre_invalido", StatusCodes.Status400BadRequest,
                "El nombre de carpeta es obligatorio (máx. 100 caracteres).");
        if (!Dominios.EsValido(body.Dominio))
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                $"Dominio '{body.Dominio}' no válido.");

        var folder = new Folder { Id = Guid.NewGuid(), Nombre = nombre, Dominio = body.Dominio.Trim().ToLowerInvariant(), CreadoUtc = DateTime.UtcNow };
        await folders.CreateAsync(folder, ct);
        return TypedResults.Created($"/api/folders", folder);
    }

    private static async Task<NoContent> DeleteAsync(Guid id, IFolderStore folders, IDocumentStore documents, CancellationToken ct)
    {
        _ = await folders.FindByIdAsync(id, ct) ?? throw new KeyNotFoundException($"Carpeta {id} no existe.");
        // las carpetas son una capa organizativa: los documentos pasan a "sin categoría"
        foreach (var doc in await documents.ListAsync(folderId: id, ct: ct))
            await documents.SetFolderAsync(doc.Id, null, ct);
        await folders.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }
}

public sealed record CreateFolderRequest(string? Nombre, string? Dominio);
