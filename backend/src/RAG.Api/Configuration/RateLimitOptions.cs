namespace RAG.Api.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";
    public bool Enabled { get; set; } = true;
    public int GeneralLimit { get; set; } = 300;
    public int ExpensiveLimit { get; set; } = 60;
    public int WindowMinutes { get; set; } = 15;
}
