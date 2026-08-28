using System.Collections.Concurrent;
using RAG.Api.Configuration;

namespace RAG.Api.Middleware;

/// <summary>
/// Rate limiting por IP con ventana fija en memoria: límite general para /api/* y un
/// límite más estricto para las rutas que gastan cuota real de LLM/embeddings
/// (upload, query, mensajes). Exento: /health y /internal.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
{
    private sealed class Contador
    {
        public DateTime VentanaInicioUtc;
        public int General;
        public int Caras;
    }

    private readonly ConcurrentDictionary<string, Contador> _contadores = new();

    public async Task InvokeAsync(HttpContext context, Microsoft.Extensions.Options.IOptions<RateLimitOptions> optionsAccessor)
    {
        var opciones = optionsAccessor.Value;

        if (!opciones.Enabled || opciones.GeneralLimit <= 0 || EsRutaExenta(context))
        {
            await next(context);
            return;
        }

        var clave = $"{context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback}";
        var ahora = DateTime.UtcNow;
        var esCara = EsRutaCara(context);

        bool superado;
        lock (_contadores.GetOrAdd(clave, _ => new Contador()))
        {
            var contador = _contadores[clave];
            if ((ahora - contador.VentanaInicioUtc).TotalMinutes >= Math.Max(opciones.WindowMinutes, 1))
            {
                contador.VentanaInicioUtc = ahora;
                contador.General = 0;
                contador.Caras = 0;
            }
            contador.General++;
            if (esCara) contador.Caras++;

            superado = contador.General > opciones.GeneralLimit
                       || (esCara && opciones.ExpensiveLimit > 0 && contador.Caras > opciones.ExpensiveLimit);
        }

        if (superado)
        {
            logger.LogWarning("Rate limit alcanzado para {IP} en {Path}", clave, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                "{\"error\":{\"code\":\"demasiadas_peticiones\",\"message\":\"Demasiadas peticiones. Inténtalo más tarde.\"}}");
            return;
        }

        await next(context);
    }

    internal static bool EsRutaExenta(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/api");

    internal static bool EsRutaCara(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) return false;
        var path = context.Request.Path;
        return path.StartsWithSegments("/api/documents/upload")
               || path.StartsWithSegments("/api/query")
               || (path.StartsWithSegments("/api/conversations") && path.Value?.EndsWith("/messages") == true);
    }
}
