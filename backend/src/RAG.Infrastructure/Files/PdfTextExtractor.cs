using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Files;

public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanHandle(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(content);
        var segmentos = new List<ExtractedSegment>();
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            segmentos.Add(new ExtractedSegment(page.Number, TextSanitizer.Sanitize(text)));
        }

        if (segmentos.Count == 0)
            throw new ExtraccionInvalidaException("El PDF no contiene texto extraíble (posible PDF escaneado).");

        return Task.FromResult<ExtractedText>(new ExtractedText(segmentos, document.NumberOfPages));
    }
}
