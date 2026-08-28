using RAG.Domain.Models;

namespace RAG.Domain.Interfaces;

public interface ITextExtractor
{
    bool CanHandle(string extension);
    Task<ExtractedText> ExtractAsync(Stream content, CancellationToken ct = default);
}

public sealed record ExtractedSegment(int? Pagina, string Texto);

public sealed record ExtractedText(IReadOnlyList<ExtractedSegment> Segmentos, int? TotalPaginas)
{
    public string TextoCompleto => string.Join("\n\n", Segmentos.Select(s => s.Texto));
}
