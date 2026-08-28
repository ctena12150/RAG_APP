using System.Threading.Channels;
using System.Threading.Tasks;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure;
using RAG.Infrastructure.Files;
using RAG.Infrastructure.RagClient;

namespace RAG.Api.Services;

public sealed class IngestionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(100);

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(documentId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed class IngestionWorker(
    IngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var documents = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
                var rag = scope.ServiceProvider.GetRequiredService<IRagService>();
                var resolver = scope.ServiceProvider.GetRequiredService<TextExtractorResolver>();

                await ProcessAsync(documentId, documents, rag, resolver, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingesta fallida para documento {DocumentoId}", documentId);
                await MarkFailedAsync(documentId, ex, stoppingToken);
            }
        }
    }

    private static async Task ProcessAsync(
        Guid documentId,
        IDocumentStore documents,
        IRagService rag,
        TextExtractorResolver resolver,
        CancellationToken ct)
    {
        var document = await documents.FindByIdAsync(documentId, ct)
            ?? throw new KeyNotFoundException($"Documento {documentId} no existe.");

        await documents.UpdateEstadoAsync(documentId, DocumentStatus.Procesando, ct: ct);

        await using var content = new MemoryStream();
        // el extractor necesita el stream; el binario se recibe desde la cola de subida en memoria
        var binary = IngestionBinaryStore.Pop(documentId)
            ?? throw new InvalidOperationException("El contenido del documento no está disponible para procesar.");
        await content.WriteAsync(binary, ct);
        content.Position = 0;

        var extension = Path.GetExtension(document.NombreArchivo);
        var extracted = await resolver.Resolve(extension).ExtractAsync(content, ct);

        _ = await rag.IngestAsync(new IngestRequest(
            document.Id,
            document.NombreArchivo,
            document.Dominio,
            [.. extracted.Segmentos.Select(s => new IngestSegment(s.Pagina, s.Texto))]), ct);

        await documents.UpdateEstadoAsync(document.Id, DocumentStatus.Listo,
            totalPaginas: extracted.TotalPaginas, ct: ct);
    }

    private async Task MarkFailedAsync(Guid documentId, Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
            var message = ex switch
            {
                RagServiceException r => r.Message,
                NotSupportedException or ExtraccionInvalidaException => ex.Message,
                _ => "Error procesando el documento."
            };
            await documents.UpdateEstadoAsync(documentId, DocumentStatus.Error, message, ct: ct);
        }
        catch (Exception inner)
        {
            logger.LogError(inner, "No se pudo marcar como fallido el documento {DocumentoId}", documentId);
        }
    }
}

public static class IngestionBinaryStore
{
    private static readonly object Lock = new();
    private static readonly Dictionary<Guid, byte[]> Pending = [];

    public static void Put(Guid documentId, byte[] bytes)
    {
        lock (Lock) Pending[documentId] = bytes;
    }

    public static byte[]? Pop(Guid documentId)
    {
        lock (Lock)
        {
            if (!Pending.Remove(documentId, out var bytes)) return null;
            return bytes;
        }
    }
}
