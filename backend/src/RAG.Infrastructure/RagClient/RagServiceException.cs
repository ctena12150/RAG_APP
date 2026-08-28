namespace RAG.Infrastructure.RagClient;

public sealed class RagServiceException : Exception
{
    public string Codigo { get; }

    public RagServiceException(string codigo, string mensaje, Exception? inner = null) : base(mensaje, inner)
        => Codigo = codigo;
}
