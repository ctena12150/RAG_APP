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
        var main = document.MainDocumentPart
            ?? throw new ExtraccionInvalidaException("El documento Word no tiene contenido legible.");
        var body = main.Document?.Body
            ?? throw new ExtraccionInvalidaException("El documento Word no tiene contenido legible.");

        var hipervinculos = main.HyperlinkRelationships
            .Where(r => Uri.TryCreate(r.Uri?.OriginalString ?? r.Uri?.ToString(), UriKind.Absolute, out _))
            .ToDictionary(r => r.Id, r => r.Uri.OriginalString);
        var parrafos = body.Descendants<Paragraph>()
            .Select(p => TextoParrafo(p, hipervinculos))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (parrafos.Count == 0)
            throw new ExtraccionInvalidaException("El documento Word no contiene texto extraíble.");

        var segmentos = new List<ExtractedSegment> { new(null, TextSanitizer.Sanitize(string.Join("\n", parrafos))) };
        return Task.FromResult<ExtractedText>(new ExtractedText(segmentos, null));
    }

    private static string TextoParrafo(Paragraph parrafo, Dictionary<string, string> hipervinculos)
    {
        var texto = parrafo.InnerText.Trim();
        foreach (var enlace in parrafo.Descendants<Hyperlink>())
        {
            if (enlace.Id?.Value is null || !hipervinculos.TryGetValue(enlace.Id.Value, out var url))
                continue;
            if (!texto.Contains(url, StringComparison.OrdinalIgnoreCase))
                texto = string.IsNullOrWhiteSpace(texto) ? url : $"{texto} ({url})";
        }
        return texto;
    }
}
