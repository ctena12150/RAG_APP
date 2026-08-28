namespace RAG.Infrastructure;

/// <summary>
/// Un extractor no pudo obtener texto útil del documento (vacío, escaneado sin OCR,
/// Word sin contenido…). El middleware la mapea a 409 con código controlado.
/// </summary>
public sealed class ExtraccionInvalidaException : Exception
{
    public ExtraccionInvalidaException(string mensaje) : base(mensaje)
    {
    }
}
