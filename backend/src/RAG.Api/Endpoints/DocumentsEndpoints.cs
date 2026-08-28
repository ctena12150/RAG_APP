using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Api.Services;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.Files;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Endpoints;

public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documentos");

        group.MapPost("/upload", UploadAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}/status", StatusAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPatch("/{id:guid}/folder", MoveToFolderAsync);

        return app;
    }

    private static async Task<Results<Created<Document>, Conflict<string>, BadRequest<ControlledException>>> UploadAsync(
        HttpRequest request,
        IDocumentStore documents,
        IFolderStore folders,
        TextExtractorResolver resolver,
        IngestionQueue queue,
        [FromServices] UploadsOptions uploads,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw new ControlledException("peticion_invalida", StatusCodes.Status400BadRequest, "Se esperaba multipart/form-data.");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            throw new ControlledException("archivo_requerido", StatusCodes.Status400BadRequest, "Falta el archivo a subir.");

        if (file.Length > uploads.MaxSizeBytes)
            throw new ControlledException("archivo_demasiado_grande", StatusCodes.Status413PayloadTooLarge,
                $"El archivo supera el límite de {uploads.MaxSizeBytes / (1024 * 1024)} MB.");

        var dominio = form["dominio"].ToString().Trim().ToLowerInvariant();
        if (!Dominios.EsValido(dominio))
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                $"El dominio '{dominio}' no es válido. Valores permitidos: {string.Join(", ", Dominios.Todos)}.");

        Guid? folderId = Guid.TryParse(form["folderId"].ToString(), out var parsedFolder) ? parsedFolder : null;
        if (folderId.HasValue)
        {
            var folder = await folders.FindByIdAsync(folderId.Value, ct);
            if (folder is null)
                throw new ControlledException("carpeta_no_encontrada", StatusCodes.Status400BadRequest, "La carpeta indicada no existe.");
            if (folder.Dominio != dominio)
                throw new ControlledException("carpeta_dominio_inconsistente", StatusCodes.Status400BadRequest,
                    "La carpeta pertenece a otro dominio.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!resolver.IsSupported(extension))
            throw new ControlledException("formato_no_soportado", StatusCodes.Status415UnsupportedMediaType,
                "Formatos soportados: .pdf, .docx, .txt, .md");

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        if (bytes.Length == 0)
            throw new ControlledException("archivo_vacio", StatusCodes.Status400BadRequest, "El archivo está vacío.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var duplicate = await documents.FindByContentHashAsync(hash, ct);
        if (duplicate is not null)
            return TypedResults.Conflict(
                $"Este contenido ya fue subido como '{duplicate.NombreArchivo}' ({duplicate.Id}).");

        var document = new Document
        {
            Id = Guid.NewGuid(),
            NombreArchivo = Path.GetFileName(file.FileName),
            Dominio = dominio,
            FolderId = folderId,
            TamanoBytes = bytes.Length,
            ContentHash = hash,
            Estado = DocumentStatus.Pendiente,
            CreadoUtc = DateTime.UtcNow
        };
        await documents.CreateAsync(document, ct);
        IngestionBinaryStore.Put(document.Id, bytes);
        await queue.EnqueueAsync(document.Id, ct);

        return TypedResults.Created($"/api/documents/{document.Id}/status", document);
    }

    private static async Task<Ok<List<DocumentDto>>> ListAsync(
        IDocumentStore documents,
        string? dominio,
        Guid? folderId,
        string? q,
        CancellationToken ct)
    {
        if (dominio is not null && !Dominios.EsValido(dominio))
            throw new ControlledException("dominio_invalido", StatusCodes.Status400BadRequest,
                $"Dominio '{dominio}' no válido.");

        folderId = folderId == Guid.Empty ? null : folderId;
        var docs = await documents.ListAsync(dominio, folderId, nombreContiene: q, ct);
        return TypedResults.Ok(docs.Select(d => DocumentDto.From(d)).ToList());
    }

    private static async Task<Ok<object>> StatusAsync(Guid id, IDocumentStore documents, CancellationToken ct)
    {
        var doc = await documents.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Documento {id} no existe.");
        return TypedResults.Ok<object>(new
        {
            id = doc.Id,
            estado = doc.Estado.ToString().ToLowerInvariant(),
            errorMensaje = doc.ErrorMensaje,
            totalPaginas = doc.TotalPaginas,
            procesadoUtc = doc.ProcesadoUtc
        });
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id, IDocumentStore documents, IRagService rag, CancellationToken ct)
    {
        var doc = await documents.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Documento {id} no existe.");
        _ = doc;

        // limpieza best-effort del índice vectorial; el borrado lógico local continúa siempre
        try { await rag.DeleteDocumentAsync(id, ct); }
        catch (RagServiceException) { }

        await documents.DeleteAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<DocumentDto>> MoveToFolderAsync(
        Guid id, MoveFolderRequest body, IDocumentStore documents, IFolderStore folders, CancellationToken ct)
    {
        var doc = await documents.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Documento {id} no existe.");

        if (body.FolderId is { } targetFolder)
        {
            var folder = await folders.FindByIdAsync(targetFolder, ct)
                ?? throw new ControlledException("carpeta_no_encontrada", StatusCodes.Status400BadRequest, "La carpeta destino no existe.");
            if (folder.Dominio != doc.Dominio)
                throw new ControlledException("carpeta_dominio_inconsistente", StatusCodes.Status400BadRequest,
                    "La carpeta pertenece a otro dominio.");
        }

        await documents.SetFolderAsync(id, body.FolderId, ct);
        doc.FolderId = body.FolderId;
        return TypedResults.Ok(DocumentDto.From(doc));
    }
}

public sealed record MoveFolderRequest(Guid? FolderId);

public sealed record DocumentDto(
    Guid Id,
    string NombreArchivo,
    string Dominio,
    Guid? FolderId,
    long TamanoBytes,
    string Estado,
    string? ErrorMensaje,
    int? TotalPaginas,
    DateTime CreadoUtc,
    DateTime? ProcesadoUtc)
{
    public static DocumentDto From(Document d) => new(
        d.Id, d.NombreArchivo, d.Dominio, d.FolderId, d.TamanoBytes,
        d.Estado.ToString().ToLowerInvariant(), d.ErrorMensaje, d.TotalPaginas,
        d.CreadoUtc, d.ProcesadoUtc);
}
