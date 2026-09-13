using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RAG.Api.Tests.Fakes;

/// <summary>Opciones del handler de prueba: autentica (crea identidad) o no (401).</summary>
public sealed class TestAuthOptions : AuthenticationSchemeOptions
{
    public bool Autenticar { get; set; }
    public string? Email { get; set; }
    public string? Nombre { get; set; }
    public string? Proveedor { get; set; }
    public string Rol { get; set; } = "usuario";
    public string[] Dominios { get; set; } = [];
}

/// <summary>
/// Handler que simula la sesión por defecto en los tests de auth (sin red, sin cookies):
/// si <see cref="TestAuthOptions.Autenticar"/> es false devuelve NoResult → 401.
/// Emite los mismos claims que el flujo OIDC real (rag:email, rag:nombre, rag:proveedor, rag:rol).
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
    public TestAuthHandler(IOptionsMonitor<TestAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Autenticar || string.IsNullOrWhiteSpace(Options.Email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("rag:email", Options.Email),
            new("rag:nombre", Options.Nombre ?? Options.Email),
            new("rag:proveedor", Options.Proveedor ?? "test"),
            new("rag:rol", Options.Rol)
        };
        claims.AddRange(Options.Dominios.Select(d => new Claim("rag:dominios", d)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}