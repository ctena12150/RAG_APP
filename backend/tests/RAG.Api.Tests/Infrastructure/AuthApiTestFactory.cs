using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RAG.Api.Tests.Fakes;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;
using RAG.Infrastructure.Stores.InMemory;

namespace RAG.Api.Tests.Infrastructure;

/// <summary>
/// Factory con Auth:Enabled=true y esquema de autenticación por defecto TestAuth.
/// <see cref="ConfigurarTestAuth"/> controla si los tests navegan autenticados o con 401.
/// </summary>
public abstract class AuthApiTestFactoryBase : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:Provider", "InMemory");
        builder.UseSetting("Security:InternalApiKey", "test-internal-key");
        builder.UseSetting("RateLimit:Enabled", "false");
        builder.UseSetting("Auth:Enabled", "true");
        builder.UseSetting("Auth:GoogleClientId", "test-google-id");
        builder.UseSetting("Auth:GoogleClientSecret", "test-google-secret");
        builder.UseSetting("Auth:LocalLoginHabilitado", "true");
        builder.UseSetting("Auth:RequireHttps", "false");
        builder.ConfigureServices(ConfigureAuthServices);
    }

    private void ConfigureAuthServices(IServiceCollection services)
    {
        // último AddAuthentication(...) gana: TestAuth pasa a ser el esquema por defecto (sin red)
        services.AddAuthentication("TestAuth")
            .AddScheme<TestAuthOptions, TestAuthHandler>("TestAuth", ConfigurarTestAuth);

        RemoveService<IUsuarioPermitidoStore>(services);
        services.AddSingleton<IUsuarioPermitidoStore>(new InMemoryUsuarioPermitidoStore([
            new UsuarioPermitido { Id = Guid.NewGuid(), Email = "carla@empresa.com", Activo = true },
            new UsuarioPermitido { Id = Guid.NewGuid(), Dominio = "empresa.com", Activo = true }
        ]));

        RemoveService<IUsuarioLocalStore>(services);
        services.AddSingleton<IUsuarioLocalStore>(new InMemoryUsuarioLocalStore([
            new UsuarioLocal
            {
                Id = Guid.NewGuid(),
                Usuario = "carla",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("ClaveTest123!"),
                Email = "carla@empresa.com",
                Nombre = "Carla Ruiz",
                Activo = true
            }
        ]));
    }

    protected virtual void ConfigurarTestAuth(TestAuthOptions opciones)
    {
        opciones.Autenticar = true;
        opciones.Email = "carla@empresa.com";
        opciones.Nombre = "Carla Ruiz";
        opciones.Proveedor = "google";
    }

    private static void RemoveService<T>(IServiceCollection services) where T : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
            if (services[i]?.ServiceType == typeof(T))
                services.RemoveAt(i);
    }
}

/// <summary>Auth habilitada + usuario autenticado (identidad de prueba válida).</summary>
public sealed class AuthApiTestFactory : AuthApiTestFactoryBase
{
}

/// <summary>Auth habilitada pero sin sesión: el esquema TestAuth responde 401.</summary>
public sealed class AuthApiTestFactorySinSesion : AuthApiTestFactoryBase
{
    protected override void ConfigurarTestAuth(TestAuthOptions opciones)
    {
        opciones.Autenticar = false;
    }
}