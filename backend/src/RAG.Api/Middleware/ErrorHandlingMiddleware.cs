using System.Text.Json;
using RAG.Infrastructure;

namespace RAG.Api.Middleware;

public sealed class ControlledException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ControlledException ex)
        {
            await WriteAsync(context, ex.Status, ex.Code, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteAsync(context, StatusCodes.Status404NotFound, "no_encontrado", ex.Message);
        }
        catch (InvalidDataException ex)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, "peticion_invalida", ex.Message);
        }
        catch (ExtraccionInvalidaException ex)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, "extraccion_invalida", ex.Message);
        }
        catch (NotSupportedException ex)
        {
            await WriteAsync(context, StatusCodes.Status415UnsupportedMediaType, "formato_no_soportado", ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // cliente desconectado: nada que reportar
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error no controlado en {Path}", context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "error_interno",
                "Ocurrió un error interno. Revise los logs del servidor.");
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(new { error = new { code, message } }, Json);
        await context.Response.WriteAsync(payload);
    }
}
