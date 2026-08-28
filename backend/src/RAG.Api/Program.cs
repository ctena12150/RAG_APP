using System.Text;
using RAG.Api.Configuration;
using RAG.Api.Endpoints;
using RAG.Api.Middleware;
using RAG.Api.Services;
using RAG.Domain.Interfaces;
using RAG.Infrastructure.Data;
using RAG.Infrastructure.Files;
using RAG.Infrastructure.RagClient;
using RAG.Infrastructure.Stores.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<RagServiceOptions>(builder.Configuration.GetSection(RagServiceOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<UploadsOptions>(builder.Configuration.GetSection(UploadsOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

// instancias de opciones en DI para poder inyectarlas con [FromServices] en minimal APIs
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadsOptions>>().Value);

if (storage.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(storage.ConnectionString));
    builder.Services.AddScoped<IDocumentStore, PostgreSqlDocumentStore>();
    builder.Services.AddScoped<IFolderStore, PostgreSqlFolderStore>();
    builder.Services.AddScoped<IConversationStore, PostgreSqlConversationStore>();
    builder.Services.AddScoped<IMessageStore, PostgreSqlMessageStore>();
}
else
{
    builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
    builder.Services.AddSingleton<IFolderStore, InMemoryFolderStore>();
    builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
    builder.Services.AddSingleton<IMessageStore, InMemoryMessageStore>();
}

builder.Services.AddSingleton<TextExtractorResolver>(_ => new TextExtractorResolver(
[
    new PdfTextExtractor(),
    new DocxTextExtractor(),
    new PlainTextExtractor()
]));

// cliente tipado vía IHttpClientFactory: pooling de handlers y refresco de DNS
builder.Services.AddHttpClient<IRagService, RagHttpClient>((sp, http) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagServiceOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, 30));
    if (!string.IsNullOrWhiteSpace(options.InternalKey))
        http.DefaultRequestHeaders.Add("X-Internal-Key", options.InternalKey);
});

// Scoped: consume IMessageStore/IConversationStore que son Scoped con proveedor PostgreSql
builder.Services.AddScoped<RagChatRelay>();
builder.Services.AddSingleton<IngestionQueue>();
builder.Services.AddHostedService<IngestionWorker>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
    if (security.AllowedOrigin == "*") policy.AllowAnyOrigin();
    else policy.WithOrigins(security.AllowedOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("X-Accel-Buffering");
}));

var app = builder.Build();

// el rate limit corre primero y escribe su propio 429 controlado
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
//app.UseHttpsRedirection();
app.UseMiddleware<InternalAuthMiddleware>();

app.MapDocuments();
app.MapFolders();
app.MapQuery();
app.MapConversations();

app.Run();

public partial class Program;
