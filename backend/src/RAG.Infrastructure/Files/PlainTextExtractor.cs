using System.Text;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Files;

public sealed class PlainTextExtractor : ITextExtractor
{
    public bool CanHandle(string extension) =>
        extension is ".txt" or ".md" or ".text" or ".markdown";

    public async Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new ExtraccionInvalidaException("El archivo de texto está vacío.");

        return new ExtractedText([new ExtractedSegment(null, TextSanitizer.Sanitize(text))], null);
    }
}
