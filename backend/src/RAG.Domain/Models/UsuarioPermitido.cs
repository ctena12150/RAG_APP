namespace RAG.Domain.Models;

/// <summary>
/// Entrada de la lista blanca de acceso al chat: o un email exacto o un dominio
/// (para permitir todo un dominio hay que registrar la parte tras la @). Nunca ambos a la vez.
/// </summary>
public sealed class UsuarioPermitido
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? Dominio { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreadoUtc { get; set; }
}