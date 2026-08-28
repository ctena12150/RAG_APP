namespace RAG.Domain.Models;

public sealed class Conversation
{
    public Guid Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public bool TituloAutomatico { get; set; }
    public IReadOnlyList<string> Dominios { get; set; } = [];
    public IReadOnlyList<Guid>? DocumentosIds { get; set; }
    public DateTime CreadoUtc { get; set; }
    public DateTime ActualizadoUtc { get; set; }
}

public sealed class Message
{
    public Guid Id { get; set; }
    public Guid ConversacionId { get; set; }
    public string Rol { get; set; } = "user";
    public string Contenido { get; set; } = string.Empty;
    public string? FuentesJson { get; set; }
    public string? TrazaJson { get; set; }
    public string? VerificacionJson { get; set; }
    public string? MetricasJson { get; set; }
    public string? RevisionContenido { get; set; }
    public DateTime CreadoUtc { get; set; }
}
