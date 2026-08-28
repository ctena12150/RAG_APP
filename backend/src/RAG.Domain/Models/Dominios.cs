namespace RAG.Domain.Models;

public static class Dominios
{
    public const string Rrhh = "rrhh";
    public const string Mantenimiento = "mantenimiento";
    public const string Onboarding = "onboarding";

    public static readonly IReadOnlyList<string> Todos = [Rrhh, Mantenimiento, Onboarding];

    public static bool EsValido(string? dominio) =>
        !string.IsNullOrWhiteSpace(dominio) && Todos.Contains(dominio.Trim().ToLowerInvariant());
}
