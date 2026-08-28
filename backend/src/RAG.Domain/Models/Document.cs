namespace RAG.Domain.Models;

public enum DocumentStatus
{
    Pendiente = 0,
    Procesando = 1,
    Listo = 2,
    Error = 3
}

public sealed class Document
{
    public Guid Id { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string Dominio { get; set; } = Dominios.Rrhh;
    public Guid? FolderId { get; set; }
    public long TamanoBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DocumentStatus Estado { get; set; } = DocumentStatus.Pendiente;
    public string? ErrorMensaje { get; set; }
    public int? TotalPaginas { get; set; }
    public DateTime CreadoUtc { get; set; }
    public DateTime? ProcesadoUtc { get; set; }
}
