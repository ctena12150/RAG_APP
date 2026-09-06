namespace RAG.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "InMemory";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class RagServiceOptions
{
    public const string SectionName = "RagService";
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string? InternalKey { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
    public int IngestTimeoutSeconds { get; set; } = 300;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string? InternalApiKey { get; set; }
    public string? AppAccessKey { get; set; }
    public string AllowedOrigin { get; set; } = "*";
}

public sealed class UploadsOptions
{
    public const string SectionName = "Uploads";
    public long MaxSizeBytes { get; set; } = 25 * 1024 * 1024;
}

/// <summary>
/// Autenticación para acceder al chat: Google (OIDC + lista blanca) o usuario/contraseña
/// local (app.usuarios). Con <see cref="Enabled"/> en false las rutas de conversaciones quedan
/// abiertas y el login se desactiva (modo dev/local). El handler de Google solo se registra
/// si existen client id + secret.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public bool Enabled { get; set; }

    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }

    /// <summary>Habilita el formulario usuario/contraseña contra app.usuarios (sin lista blanca).</summary>
    public bool LocalLoginHabilitado { get; set; }

    public string CallbackPath { get; set; } = "/auth/callback";
    public string DenegadoPath { get; set; } = "/auth/denegado";
    public bool RequireHttps { get; set; } = true;
    public int SesionHoras { get; set; } = 12;

    /// <summary>Semilla inicial de la lista blanca (emails o dominios, separados por coma). Vacío = no sembrar.</summary>
    public string? WhitelistEmails { get; set; }
    public string? WhitelistDomains { get; set; }

    public bool ProveedorGoogleConfigurado =>
        !string.IsNullOrWhiteSpace(GoogleClientId) && !string.IsNullOrWhiteSpace(GoogleClientSecret);
}
