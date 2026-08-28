using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RAG.Api.Services;
using RAG.Domain.Interfaces;
using RAG.Api.Tests.Fakes;

namespace RAG.Api.Tests.Infrastructure;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public FakeRagService Rag { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:Provider", "InMemory");
        builder.UseSetting("Security:InternalApiKey", "test-internal-key");
        // los tests de guardrails de entrada usan su propia factory con límites pequeños
        builder.UseSetting("RateLimit:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            RemoveService<IRagService>(services);
            services.AddSingleton<IRagService>(Rag);
        });
    }

    private static void RemoveService<T>(IServiceCollection services) where T : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
            if (services[i]?.ServiceType == typeof(T))
                services.RemoveAt(i);
    }
}
