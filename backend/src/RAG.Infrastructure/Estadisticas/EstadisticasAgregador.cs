using System.Text.Json;
using RAG.Domain.Models;

namespace RAG.Infrastructure.Estadisticas;

public static class EstadisticasAgregador
{
    public const string FraseAbstencion = "No dispongo de esa documentación";

    public static EstadisticasResumen Resumir(
        IReadOnlyList<Message> usuarios, IReadOnlyList<Message> asistentes,
        IReadOnlyDictionary<Guid, Conversation> conversaciones)
    {
        bool EsAbstencion(Message m) =>
            m.Contenido.Contains(FraseAbstencion, StringComparison.OrdinalIgnoreCase);
        var votosBien = asistentes.Count(m => m.Voto == "bien");
        var votosMal = asistentes.Count(m => m.Voto == "mal");
        var votados = votosBien + votosMal;
        var pct = votados == 0 ? 0 : Math.Round(votosBien * 100.0 / votados, 1);
        var sinCobertura = asistentes.Count(EsAbstencion);
        var malCubiertas = asistentes.Count(m => m.Voto == "mal" && !EsAbstencion(m));
        var cubiertas = Math.Max(usuarios.Count - sinCobertura, 0);
        var sirvieron = Math.Max(cubiertas - malCubiertas, 0);
        var pctResueltas = usuarios.Count == 0 ? 0 : Math.Round(sirvieron * 100.0 / usuarios.Count, 1);

        var tiempos = asistentes
            .Select(m => ExtraerTotalMs(m.MetricasJson))
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        var porDominio = usuarios
            .SelectMany(m => DominiosDe(m, conversaciones))
            .GroupBy(d => d)
            .Select(g => new ConteoDominio(g.Key, g.Count()))
            .OrderByDescending(c => c.Consultas)
            .ToList();

        var porDia = usuarios
            .GroupBy(m => m.CreadoUtc.ToUniversalTime().ToString("yyyy-MM-dd"))
            .Select(g => new ConteoDia(g.Key, g.Count()))
            .OrderBy(c => c.Dia)
            .ToList();

        var docs = asistentes
            .SelectMany(m => DocumentosCitados(m.FuentesJson))
            .GroupBy(d => d)
            .Select(g => new DocumentoCitado(g.Key, g.Count()))
            .OrderByDescending(c => c.Citas)
            .Take(10)
            .ToList();

        var top = usuarios
            .GroupBy(m => Normalizar(m.Contenido))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new PreguntaTop(g.OrderByDescending(m => m.CreadoUtc).First().Contenido.Trim(), g.Count()))
            .OrderByDescending(p => p.Veces)
            .Take(10)
            .ToList();

        List<MuestraConsulta> Muestras(IEnumerable<Message> msgs) => msgs
            .OrderByDescending(m => m.CreadoUtc)
            .Take(8)
            .Select(m => new MuestraConsulta(
                Recortar(m.Contenido, 200),
                DominiosDe(m, conversaciones).FirstOrDefault(),
                m.CreadoUtc))
            .ToList();

        return new EstadisticasResumen(
            usuarios.Count, votosBien, votosMal, asistentes.Count - votados, pct, sinCobertura,
            cubiertas, sirvieron, pctResueltas,
            tiempos.Count == 0 ? null : Math.Round(tiempos.Average(), 1),
            porDominio, porDia, docs, top,
            Muestras(asistentes.Where(m => m.Voto == "mal")),
            Muestras(asistentes.Where(m => m.Contenido.Contains(FraseAbstencion, StringComparison.OrdinalIgnoreCase))));
    }

    private static IEnumerable<string> DominiosDe(Message m, IReadOnlyDictionary<Guid, Conversation> convs)
    {
        if (convs.TryGetValue(m.ConversacionId, out var c) && c.Dominios is { Count: > 0 })
            foreach (var d in c.Dominios)
                yield return d;
        else
            yield return "general";
    }

    private static string Normalizar(string s) =>
        string.Join(" ", s.Trim().ToLowerInvariant().Split((char[])[' ', '\n', '\r', '\t'],
            StringSplitOptions.RemoveEmptyEntries));

    private static string Recortar(string s, int max)
    {
        var t = s.Trim().ReplaceLineEndings(" ");
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static IEnumerable<string> DocumentosCitados(string? fuentesJson)
    {
        if (string.IsNullOrWhiteSpace(fuentesJson)) yield break;
        List<SourceCard>? lista;
        try
        {
            lista = JsonSerializer.Deserialize<List<SourceCard>>(fuentesJson, RagJson.Options);
        }
        catch (JsonException)
        {
            yield break;
        }
        if (lista is null) yield break;
        foreach (var f in lista)
            if (!string.IsNullOrWhiteSpace(f.DocumentoNombre))
                yield return f.DocumentoNombre;
    }

    private static double? ExtraerTotalMs(string? metricasJson)
    {
        if (string.IsNullOrWhiteSpace(metricasJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metricasJson);
            if (doc.RootElement.TryGetProperty("totalMs", out var v) && v.TryGetDouble(out var d)) return d;
            if (doc.RootElement.TryGetProperty("total_ms", out var v2) && v2.TryGetDouble(out var d2)) return d2;
        }
        catch (JsonException)
        {
        }
        return null;
    }
}
