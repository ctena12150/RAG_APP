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
