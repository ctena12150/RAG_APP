namespace RAG.Api.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public bool Enabled { get; set; } = true;
    public int GeneralLimit { get; set; } = 300;
    public int ExpensiveLimit { get; set; } = 60;
    public int WindowMinutes { get; set; } = 15;

    /// <summary>
    /// Actívalo SOLO cuando el backend va detrás de un proxy inverso de confianza
    /// (p. ej. nginx en deploy/docker-compose.yml): la clave de limitación pasa a ser
    /// la primera IP de X-Forwarded-For en lugar de la IP del propio proxy.
    /// </summary>
    public bool TrustProxyHeaders { get; set; }
}
