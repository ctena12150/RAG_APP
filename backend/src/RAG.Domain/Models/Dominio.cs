namespace RAG.Domain.Models;

public sealed class Dominio
{
    public string Clave { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime CreadoUtc { get; set; }
}
