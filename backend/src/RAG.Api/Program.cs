using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

// instancias de opciones en DI para poder inyectarlas con [FromServices] en minimal APIs
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadsOptions>>().Value);

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(auth);

if (storage.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(storage.ConnectionString));
    builder.Services.AddScoped<IDocumentStore, PostgreSqlDocumentStore>();
    builder.Services.AddScoped<IFolderStore, PostgreSqlFolderStore>();
    builder.Services.AddScoped<IConversationStore, PostgreSqlConversationStore>();
    builder.Services.AddScoped<IMessageStore, PostgreSqlMessageStore>();
    builder.Services.AddScoped<IUsuarioPermitidoStore, PostgreSqlUsuarioPermitidoStore>();
    builder.Services.AddScoped<IUsuarioLocalStore, PostgreSqlUsuarioLocalStore>();
}
else
{
    builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
    builder.Services.AddSingleton<IFolderStore, InMemoryFolderStore>();
    builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
    builder.Services.AddSingleton<IMessageStore, InMemoryMessageStore>();
    builder.Services.AddSingleton<IUsuarioPermitidoStore>(new InMemoryUsuarioPermitidoStore());
    builder.Services.AddSingleton<IUsuarioLocalStore>(new InMemoryUsuarioLocalStore());
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

// OIDC (Google) + cookie de sesión para acceder al chat. Solo con Auth:Enabled;
// el proveedor Google se registra únicamente si hay credenciales configuradas.
if (auth.Enabled)
{
    builder.Services.AddAuthorization();
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o => AuthRegistration.ConfigurarCookie(o, auth))
        .AddGoogleOidc(auth);
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
    if (security.AllowedOrigin == "*") policy.AllowAnyOrigin();
    else policy.WithOrigins(security.AllowedOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("X-Accel-Buffering");
}));

var app = builder.Build();

// El backend vive tras Caddy→nginx: el esquema real llega por X-Forwarded-Proto (necesario
// para que OIDC construya el redirect_uri https y para la cookie Secure).
// Espera siempre detrás de un proxy de confianza; ratelimit ya aprovecha X-Forwarded-For.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (auth.Enabled)
{
    using var scope = app.Services.CreateScope();
    try
    {
        var whitelist = scope.ServiceProvider.GetRequiredService<IUsuarioPermitidoStore>();
        await ListaBlancaSeeder.SembrarAsync(whitelist, auth);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "No se pudo sembrar la lista blanca desde configuración.");
    }
}

// el rate limit corre primero y escribe su propio 429 controlado
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();

if (auth.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();
//app.UseHttpsRedirection();
app.UseMiddleware<InternalAuthMiddleware>();

app.MapDocuments();
app.MapFolders();
app.MapQuery();
app.MapConversations(exigirAuth: auth.Enabled);
app.MapAuth(auth);

app.Run();

public partial class Program;
