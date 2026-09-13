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
    private const int MaxIntentos = 3;
    private static readonly Dictionary<Guid, int> Intentos = new();

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
                lock (Intentos) Intentos.Remove(documentId);
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
        // el extractor necesita el stream; el binario se consulta sin consumir para
        // poder reintentar si el servicio RAG no está disponible
        var binary = IngestionBinaryStore.Peek(documentId)
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

        IngestionBinaryStore.Pop(documentId);
        await documents.UpdateEstadoAsync(document.Id, DocumentStatus.Listo,
            totalPaginas: extracted.TotalPaginas, ct: ct);
    }

    private async Task MarkFailedAsync(Guid documentId, Exception ex, CancellationToken ct)
    {
        int intento;
        lock (Intentos) intento = Intentos[documentId] = Intentos.GetValueOrDefault(documentId) + 1;
        // sin binario (reinicio con cola persistida) o tras agotar intentos: error definitivo
        if (IngestionBinaryStore.Peek(documentId) is not null && intento < MaxIntentos && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Reintentando ingesta de {DocumentoId} (intento {Intento}/{Max})", documentId, intento + 1, MaxIntentos);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var documents = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
                await documents.UpdateEstadoAsync(documentId, DocumentStatus.Pendiente, ct: ct);
                await queue.EnqueueAsync(documentId, ct);
                return;
            }
            catch (Exception inner)
            {
                logger.LogError(inner, "No se pudo reencolar el documento {DocumentoId}", documentId);
            }
        }
        IngestionBinaryStore.Pop(documentId);
        lock (Intentos) Intentos.Remove(documentId);
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

    public static byte[]? Peek(Guid documentId)
    {
        lock (Lock) return Pending.GetValueOrDefault(documentId);
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
