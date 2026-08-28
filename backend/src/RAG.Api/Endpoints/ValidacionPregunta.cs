using System.Text.RegularExpressions;
using RAG.Api.Middleware;

namespace RAG.Api.Endpoints;

public static partial class ValidacionPregunta
{
    public const int LongitudMaxima = 2000;

    [GeneratedRegex(
        @"ignora\s+(las\s+)?(instrucciones|reglas|restricciones)"
        + @"|ignore\s+(previous|all)\s+instructions"
        + @"|disregard\s+(all\s+)?(previous\s+)?instructions"
        + @"|revel(a|e|ar)\s+(tu|su|el)?\s*(prompt|instrucciones|system)"
        + @"|(mu\u00e9stra|ens\u00e9\u00f1a)me\s+(tu|el)\s+(prompt|system)"
        + @"|\bsystem\s*prompt\b"
        + @"|sin\s+(restricciones|l\u00edmites|filtros)\b"
        + @"|developer\s+mode\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PatronJailbreak();

    /// <summary>Valida longitud e intentos de manipulación; lanza errores controlados.</summary>
    public static void Validar(string? pregunta)
    {
        if (string.IsNullOrWhiteSpace(pregunta)) return; // la obligatoriedad se valida en cada endpoint

        if (pregunta.Length > LongitudMaxima)
            throw new ControlledException("pregunta_demasiado_larga", StatusCodes.Status400BadRequest,
                $"La pregunta supera el máximo de {LongitudMaxima} caracteres.");

        if (PatronJailbreak().IsMatch(pregunta))
            throw new ControlledException("consulta_no_permitida", StatusCodes.Status400BadRequest,
                "La consulta no está permitida. Formula tu pregunta sobre los documentos.");
    }
}
