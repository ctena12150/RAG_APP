using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Files;

public sealed class DocxTextExtractor : ITextExtractor
{
    public bool CanHandle(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new ExtraccionInvalidaException("El documento Word no tiene contenido legible.");

        var parrafos = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (parrafos.Count == 0)
            throw new ExtraccionInvalidaException("El documento Word no contiene texto extraíble.");

        var segmentos = new List<ExtractedSegment> { new(null, TextSanitizer.Sanitize(string.Join("\n", parrafos))) };
        return Task.FromResult<ExtractedText>(new ExtractedText(segmentos, null));
    }
}
