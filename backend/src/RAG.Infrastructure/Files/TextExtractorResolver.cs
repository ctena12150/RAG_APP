using RAG.Domain.Interfaces;

namespace RAG.Infrastructure.Files;

public sealed class TextExtractorResolver(IEnumerable<ITextExtractor> extractors)
{
    private readonly IReadOnlyList<ITextExtractor> _extractors = extractors.ToList();

    public ITextExtractor Resolve(string extension)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(extension));
        if (extractor is null)
            throw new NotSupportedException($"Formato de archivo no soportado: '{extension}'. Soportados: .pdf, .docx, .txt, .md");
        return extractor;
    }

    public bool IsSupported(string extension) => _extractors.Any(e => e.CanHandle(extension));
}
