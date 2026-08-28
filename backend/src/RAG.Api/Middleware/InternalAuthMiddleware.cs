using System.Text;
using RAG.Api.Configuration;

namespace RAG.Api.Middleware;

/// <summary>
/// Autenticación de servicio a servicio: /internal exige X-Internal-Key; /api exige
/// X-App-Access-Key solo si Security:AppAccessKey está configurado.
/// </summary>
public sealed class InternalAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, Microsoft.Extensions.Options.IOptions<SecurityOptions> optionsAccessor)
    {
        var security = optionsAccessor.Value;
        var path = context.Request.Path;

        if (path.StartsWithSegments("/internal"))
        {
            if (string.IsNullOrWhiteSpace(security.InternalApiKey) ||
                !string.Equals(context.Request.Headers["X-Internal-Key"], security.InternalApiKey, StringComparison.Ordinal))
            {
                await Escribir401Async(context, "Clave interna ausente o inválida.");
                return;
            }
        }
        else if (path.StartsWithSegments("/api") && !string.IsNullOrWhiteSpace(security.AppAccessKey))
        {
            if (!string.Equals(context.Request.Headers["X-App-Access-Key"], security.AppAccessKey, StringComparison.Ordinal))
            {
                await Escribir401Async(context, "Clave de acceso ausente o inválida.");
                return;
            }
        }

        await next(context);
    }

    private static async Task Escribir401Async(HttpContext context, string mensaje)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            $"{{\"error\":{{\"code\":\"no_autorizado\",\"message\":\"{mensaje}\"}}}}", Encoding.UTF8);
    }
}
