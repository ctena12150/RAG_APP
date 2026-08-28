namespace RAG.Domain.Models;

public sealed class Folder
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Dominio { get; set; } = Dominios.Rrhh;
    public DateTime CreadoUtc { get; set; }
}
