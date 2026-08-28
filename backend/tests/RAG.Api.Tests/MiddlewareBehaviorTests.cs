using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RAG.Api.Configuration;
using RAG.Api.Middleware;
using RAG.Infrastructure;
using System.Text.Json;

namespace RAG.Api.Tests;

/// <summary>Comportamiento del manejo de errores y autenticación interna, sin host HTTP.</summary>
public sealed class MiddlewareBehaviorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<(int Status, JsonElement Body)> EjecutarConExcepcionAsync(Exception excepcion)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw excepcion,
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = JsonSerializer.Deserialize<JsonElement>(await reader.ReadToEndAsync());
        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task Extraccion_invalida_se_mapea_a_409_controlado()
    {
        var (status, body) = await EjecutarConExcepcionAsync(
            new ExtraccionInvalidaException("El PDF no contiene texto extraíble."));
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("extraccion_invalida", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidOperationException_generica_no_se_disfraza_de_conflicto()
    {
        // un fallo lógico inesperado debe ser 500, nunca 409 por heurística de mensaje
        var (status, body) = await EjecutarConExcepcionAsync(
            new InvalidOperationException("fallo interno cualquiera"));
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("error_interno", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task KeyNotFound_devuelve_404_y_ControlledException_respeta_codigo()
    {
        var (status404, body404) = await EjecutarConExcepcionAsync(new KeyNotFoundException("Documento X no existe."));
        Assert.Equal(StatusCodes.Status404NotFound, status404);
        Assert.Contains("Documento X", body404.GetProperty("error").GetProperty("message").GetString());

        var (status400, body400) = await EjecutarConExcepcionAsync(
            new ControlledException("dominio_invalido", 400, "Dominio no válido."));
        Assert.Equal(400, status400);
        Assert.Equal("dominio_invalido", body400.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<int> EjecutarAuthAsync(string path, string? internalKey, string? appKey)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (internalKey is not null) context.Request.Headers["X-Internal-Key"] = internalKey;
        if (appKey is not null) context.Request.Headers["X-App-Access-Key"] = appKey;

        var options = Options.Create(new SecurityOptions { InternalApiKey = "secreta", AppAccessKey = null });
        var middleware = new InternalAuthMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, options);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task Internal_sin_clave_valida_da_401_y_con_clave_pasa()
    {
        Assert.Equal(StatusCodes.Status401Unauthorized, await EjecutarAuthAsync("/internal/chunks/search", null, null));
        Assert.Equal(StatusCodes.Status401Unauthorized, await EjecutarAuthAsync("/internal/chunks/search", "mala", null));
        Assert.Equal(StatusCodes.Status200OK, await EjecutarAuthAsync("/internal/chunks/search", "secreta", null));
    }

    [Fact]
    public async Task Api_sin_AppAccessKey_configurada_esta_abierta()
    {
        Assert.Equal(StatusCodes.Status200OK, await EjecutarAuthAsync("/api/documents", null, null));
    }
}
