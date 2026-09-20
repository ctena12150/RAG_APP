using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RAG.Infrastructure.Files;

namespace RAG.Api.Tests;

public sealed class ExtractoresTests
{
    [Fact]
    public async Task Docx_con_hipervinculo_conserva_la_url()
    {
        using var ms = new MemoryStream();
        using (var document = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var url = "https://cuenta.blob.core.windows.net/videos/a1b2.mp4";
            var relacion = main.AddHyperlinkRelationship(new Uri(url), true);
            var parrafo = new Paragraph();
            var enlace = new Hyperlink { Id = relacion.Id };
            enlace.Append(new Run(new Text("Ver vídeo de bienvenida")));
            parrafo.Append(enlace);
            main.Document.Body!.Append(parrafo);
            main.Document.Save();
        }
        ms.Position = 0;

        var extractor = new DocxTextExtractor();
        var resultado = await extractor.ExtractAsync(ms);

        var texto = string.Join("\n", resultado.Segmentos.Select(s => s.Texto));
        Assert.Contains("Ver vídeo de bienvenida", texto);
        Assert.Contains("https://cuenta.blob.core.windows.net/videos/a1b2.mp4", texto);
    }
}
