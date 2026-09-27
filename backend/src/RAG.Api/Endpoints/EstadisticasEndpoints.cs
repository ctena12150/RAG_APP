using Microsoft.AspNetCore.Http.HttpResults;
using RAG.Api.Configuration;
using RAG.Domain.Interfaces;

namespace RAG.Api.Endpoints;

public static class EstadisticasEndpoints
{
    public static IEndpointRouteBuilder MapEstadisticas(this IEndpointRouteBuilder app, bool exigirAuth = false)
    {
        var group = app.MapGroup("/api/estadisticas").WithTags("Estadísticas");
        if (exigirAuth) group.RequireAuthorization(AuthRegistration.PoliticaSuperUsuario);
        group.MapGet("/", ResumenAsync);
        return app;
    }

    private static async Task<Ok<object>> ResumenAsync(
        string? desde, string? hasta, IEstadisticasStore store, CancellationToken ct)
    {
        var d = ParseFecha(desde);
        var h = ParseFecha(hasta);
        var resumen = await store.ResumenAsync(d, h, ct);
        return TypedResults.Ok<object>(resumen);
    }

    private static DateTime? ParseFecha(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return DateTime.TryParse(valor, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToUniversalTime()
            : null;
    }
}
