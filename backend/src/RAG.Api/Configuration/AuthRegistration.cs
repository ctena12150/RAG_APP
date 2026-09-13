using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
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
    public const string ClaimRol = "rag:rol";
    public const string ClaimDominios = "rag:dominios";

    /// <summary>Política: puede subir y gestionar documentos/carpetas (teamleader o superusuario).</summary>
    public const string PoliticaEquipo = "equipo";

    /// <summary>Política: administración de usuarios locales (solo superusuario).</summary>
    public const string PoliticaSuperUsuario = "superusuario";

    public static void ConfigurarPoliticas(AuthorizationOptions options)
    {
        options.AddPolicy(PoliticaEquipo, p => p.RequireClaim(ClaimRol, Roles.TeamLeader, Roles.SuperUsuario));
        options.AddPolicy(PoliticaSuperUsuario, p => p.RequireClaim(ClaimRol, Roles.SuperUsuario));
    }

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
            new(ClaimProveedor, proveedor),
            // solo el login local gestiona roles; Google (lista blanca) entra siempre como usuario
            new(ClaimRol, Roles.Usuario)
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
    /// claims que el flujo OIDC de Google para que /auth/me y el chat funcionen igual,
    /// más el claim de rol (la autorización por rol solo existe en el login local).
    /// </summary>
    internal static ClaimsPrincipal PrincipalLocal(UsuarioLocal usuario)
    {
        var claims = new List<Claim>
        {
            new(ClaimEmail, (usuario.Email ?? usuario.Usuario)),
            new(ClaimProveedor, ProveedorLocal),
            new(ClaimRol, Roles.EsValido(usuario.Rol) ? usuario.Rol.Trim().ToLowerInvariant() : Roles.Usuario)
        };
        foreach (var dominio in DominiosEfectivos(usuario.Rol, usuario.Dominios))
            claims.Add(new Claim(ClaimDominios, dominio));
        if (!string.IsNullOrWhiteSpace(usuario.Nombre))
            claims.Add(new Claim(ClaimNombre, usuario.Nombre));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "rag.local"));
    }

    /// <summary>
    /// Dominios que un rol gestiona. El superusuario siempre los tiene todos; el teamleader
    /// sin dominios asignados también (lista vacía = todos). Solo el teamleader con lista
    /// explícita queda acotado. El rol usuario no gestiona ningún dominio.
    /// </summary>
    internal static IReadOnlyList<string> DominiosEfectivos(string? rol, string[]? dominios)
    {
        var normalizado = Roles.EsValido(rol) ? rol!.Trim().ToLowerInvariant() : Roles.Usuario;
        if (normalizado == Roles.SuperUsuario) return Dominios.Todos;
        if (normalizado == Roles.TeamLeader)
            return dominios is { Length: > 0 } ? dominios : Dominios.Todos;
        return [];
    }

    /// <summary>
    /// Dominios de gestión del principal (claims rag:dominios). Null = sin restricción
    /// (auth desactivada o superusuario); lista = solo esos dominios.
    /// </summary>
    internal static IReadOnlyList<string>? DominiosDelPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var rol = RolDelPrincipal(principal);
        if (rol == Roles.SuperUsuario) return null;
        var dominios = principal.FindAll(ClaimDominios).Select(c => c.Value).Distinct().ToList();
        return dominios.Count == 0 ? null : dominios;
    }

    /// <summary>
    /// True si el principal puede gestionar documentos/carpetas del dominio indicado.
    /// Lanza <see cref="ControlledException"/> (403 dominio_no_permitido) en caso contrario.
    /// </summary>
    internal static void ExigirDominioGestionado(ClaimsPrincipal user, string dominio)
    {
        var permitidos = DominiosDelPrincipal(user);
        if (permitidos is null) return;
        if (!permitidos.Contains(dominio.Trim().ToLowerInvariant()))
            throw new Middleware.ControlledException("dominio_no_permitido", StatusCodes.Status403Forbidden,
                $"No tienes acceso de gestión al dominio '{dominio}'.");
    }

    /// <summary>Rol del principal autenticado, o <see cref="Roles.Usuario"/> si no trae claim.</summary>
    internal static string RolDelPrincipal(ClaimsPrincipal? principal)
    {
        var rol = principal?.FindFirst(ClaimRol)?.Value;
        return Roles.EsValido(rol) ? rol!.Trim().ToLowerInvariant() : Roles.Usuario;
    }
}