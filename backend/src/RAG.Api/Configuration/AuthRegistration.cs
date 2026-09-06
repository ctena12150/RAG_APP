using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RAG.Domain.Interfaces;
using RAG.Domain.Models;

namespace RAG.Api.Configuration;

/// <summary>
/// Registro de los esquemas de autenticación: cookie + OIDC Google y login local
/// (usuario/contraseña contra IUsuarioLocalStore). Google usa el handler OpenIdConnect del
/// framework y se registra solo si hay credenciales; su email se valida contra la lista blanca
/// (IUsuarioPermitidoStore) justo cuando el proveedor devuelve el id_token.
/// </summary>
internal static class AuthRegistration
{
    public const string SchemeGoogle = "Google";
    public const string ProveedorLocal = "local";
    public const string ClaimEmail = "rag:email";
    public const string ClaimNombre = "rag:nombre";
    public const string ClaimProveedor = "rag:proveedor";

    public static void ConfigurarCookie(CookieAuthenticationOptions options, AuthOptions auth)
    {
        options.Cookie.Name = "rag.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = auth.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Max(auth.SesionHoras, 1));
        options.SlidingExpiration = true;
        options.LoginPath = "/";
        options.AccessDeniedPath = auth.DenegadoPath;

        // SPA same-origin: /api responde 401/403 en JSON sin redirect (fetch no sigue 302).
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    }

    public static AuthenticationBuilder AddGoogleOidc(this AuthenticationBuilder builder, AuthOptions auth)
    {
        if (!auth.ProveedorGoogleConfigurado) return builder;
        return builder.AddOpenIdConnect(SchemeGoogle, o =>
        {
            o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.Authority = "https://accounts.google.com";
            o.ClientId = auth.GoogleClientId!;
            o.ClientSecret = auth.GoogleClientSecret!;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.UsePkce = true;
            o.MapInboundClaims = false;
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("email");
            o.Scope.Add("profile");
            o.CallbackPath = new PathString(auth.CallbackPath + "-google");
            o.AccessDeniedPath = new PathString(auth.DenegadoPath);
            o.SaveTokens = false;
            o.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context => ValidarListaBlancaAsync(context, SchemeGoogle)
            };
        });
    }

    private static async Task ValidarListaBlancaAsync(TokenValidatedContext context, string proveedor)
    {
        var email = EmailDelPrincipal(context.Principal);
        if (string.IsNullOrWhiteSpace(email))
        {
            context.Fail("El proveedor de identidad no devolvió un correo válido.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IUsuarioPermitidoStore>();
        if (!await store.EstaPermitidoAsync(email, context.HttpContext.RequestAborted))
        {
            context.Fail("El correo no está en la lista blanca de acceso al chat.");
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimEmail, email),
            new(ClaimProveedor, proveedor)
        };
        var nombre = context.Principal?.FindFirst("name")?.Value;
        if (!string.IsNullOrWhiteSpace(nombre)) claims.Add(new Claim(ClaimNombre, nombre));

        context.Principal?.AddIdentity(new ClaimsIdentity(claims, "rag.lista_blanca"));
    }

    /// <summary>
    /// Email normalizado del principal OIDC: Google lo entrega en "email".
    /// </summary>
    internal static string? EmailDelPrincipal(ClaimsPrincipal? principal)
    {
        if (principal is null) return null;
        foreach (var tipo in new[] { "email", "preferred_username", "upn", ClaimTypes.Email })
        {
            var valor = principal.FindFirst(tipo)?.Value;
            if (!string.IsNullOrWhiteSpace(valor) && valor.Contains('@'))
                return valor.Trim().ToLowerInvariant();
        }
        return null;
    }

    internal static string NombreDelPrincipal(ClaimsPrincipal? principal)
    {
        var nombre = principal?.FindFirst(ClaimNombre)?.Value
            ?? principal?.FindFirst("name")?.Value
            ?? principal?.Identity?.Name;
        return string.IsNullOrWhiteSpace(nombre) ? "Usuario" : nombre;
    }

    /// <summary>
    /// Principal de la cookie para un usuario local (usuario/contraseña). Emite los mismos
    /// claims que el flujo OIDC de Google para que /auth/me y el chat funcionen igual.
    /// </summary>
    internal static ClaimsPrincipal PrincipalLocal(UsuarioLocal usuario)
    {
        var claims = new List<Claim> { new(ClaimEmail, (usuario.Email ?? usuario.Usuario)), new(ClaimProveedor, ProveedorLocal) };
        if (!string.IsNullOrWhiteSpace(usuario.Nombre))
            claims.Add(new Claim(ClaimNombre, usuario.Nombre));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "rag.local"));
    }
}