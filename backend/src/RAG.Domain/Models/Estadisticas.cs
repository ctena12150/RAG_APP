namespace RAG.Domain.Models;

public sealed record EstadisticasResumen(
    int TotalConsultas,
    int VotosBien,
    int VotosMal,
    int SinVoto,
    double PctSatisfaccion,
    int SinCobertura,
    int Cubiertas,
    int Sirvieron,
    double PctResueltas,
    double? TiempoMedioMs,
    IReadOnlyList<ConteoDominio> PorDominio,
    IReadOnlyList<ConteoDia> PorDia,
    IReadOnlyList<DocumentoCitado> DocumentosMasCitados,
    IReadOnlyList<PreguntaTop> TopPreguntas,
    IReadOnlyList<MuestraConsulta> UltimasMalas,
    IReadOnlyList<MuestraConsulta> UltimasSinCobertura);

public sealed record ConteoDominio(string Dominio, int Consultas);
public sealed record ConteoDia(string Dia, int Consultas);
public sealed record DocumentoCitado(string Documento, int Citas);
public sealed record PreguntaTop(string Pregunta, int Veces);
public sealed record MuestraConsulta(string Pregunta, string? Dominio, DateTime CreadoUtc);
