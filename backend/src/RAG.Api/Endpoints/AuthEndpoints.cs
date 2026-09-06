using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Domain.Interfaces;

namespace RAG.Api.Endpoints;

/// <summary>
/// Flujo de acceso al chat: login Google (OIDC + lista blanca), login local (usuario/contraseña
/// contra app.usuarios), cierre de sesión y estado de la sesión (/auth/me) que consume el frontend.
/// </summary>
public static class AuthEndpoints
{
    private static readonly AuthenticationProperties RedirigirARaiz = new() { RedirectUri = "/" };

    internal sealed record LoginLocalRequest(string? Usuario, string? Contrasena);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app, AuthOptions auth)
    {
        var group = app.MapGroup("/auth").WithTags("Autenticación");

        group.MapGet("/login/google", () => Results.Challenge(RedirigirARaiz, [AuthRegistration.SchemeGoogle]));
        group.MapPost("/login", async (LoginLocalRequest request, HttpContext http, CancellationToken ct) =>
            await LoginLocalAsync(request, http, auth, ct));
        group.MapGet("/signout", () => Results.SignOut(RedirigirARaiz, [CookieAuthenticationDefaults.AuthenticationScheme]));
        group.MapGet("/me", (ClaimsPrincipal user) => MeAsync(user, auth));
        group.MapGet("/denegado", () => Results.Json(
            new { error = new { code = "acceso_denegado", message = "No estás autorizado a usar el chat." } },
            statusCode: StatusCodes.Status403Forbidden));

        return app;
    }

    /// <summary>
    /// Login local (usuario/contraseña): verifica BCrypt contra <see cref="IUsuarioLocalStore"/> y
    /// emite la cookie de sesión con los claims habituales. No pasa por la lista blanca: el alta en
    /// app.usuarios (con hash) ya es la autorización. Respeta la forma de /auth/me para que el
    /// frontend use directamente el resultado.
    /// </summary>
    private static async Task<IResult> LoginLocalAsync(LoginLocalRequest request, HttpContext http, AuthOptions auth, CancellationToken ct)
    {
        if (!auth.LocalLoginHabilitado)
            throw new ControlledException("login_local_deshabilitado", StatusCodes.Status403Forbidden,
                "El acceso con usuario y contraseña está deshabilitado.");

        var usuario = request.Usuario?.Trim();
        var contrasena = request.Contrasena;
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(contrasena))
            throw new ControlledException("credenciales_invalidas", StatusCodes.Status401Unauthorized, "Credenciales inválidas.");

        var store = http.RequestServices.GetRequiredService<IUsuarioLocalStore>();
        var local = await store.ObtenerPorUsuarioAsync(usuario, ct);
        if (local is null || string.IsNullOrWhiteSpace(local.PasswordHash) ||
            !BCrypt.Net.BCrypt.Verify(contrasena, local.PasswordHash))
            throw new ControlledException("credenciales_invalidas", StatusCodes.Status401Unauthorized, "Credenciales inválidas.");

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AuthRegistration.PrincipalLocal(local),
            new AuthenticationProperties { IsPersistent = true });

        return Results.Ok(new
        {
            autenticado = true,
            email = local.Email ?? usuario,
            nombre = string.IsNullOrWhiteSpace(local.Nombre) ? usuario : local.Nombre,
            proveedor = AuthRegistration.ProveedorLocal,
            proveedores = ProveedoresDisponibles(auth)
        });
    }

    private static IResult MeAsync(ClaimsPrincipal user, AuthOptions auth)
    {
        var email = user.FindFirst(AuthRegistration.ClaimEmail)?.Value;
        var autenticado = user.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(email);

        return Results.Ok(new
        {
            autenticado,
            email = autenticado ? email : null,
            nombre = autenticado ? AuthRegistration.NombreDelPrincipal(user) : null,
            proveedor = autenticado ? user.FindFirst(AuthRegistration.ClaimProveedor)?.Value : null,
            proveedores = ProveedoresDisponibles(auth)
        });
    }

    private static string[] ProveedoresDisponibles(AuthOptions auth)
    {
        var proveedores = new List<string>();
        if (auth.ProveedorGoogleConfigurado) proveedores.Add("google");
        if (auth.LocalLoginHabilitado) proveedores.Add("local");
        return [.. proveedores];
    }
}