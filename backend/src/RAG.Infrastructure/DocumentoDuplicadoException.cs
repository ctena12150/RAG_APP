namespace RAG.Infrastructure;

/// <summary>
/// El content_hash ya existe en otro documento (carrera entre el check previo y el
/// INSERT, o índice UNIQUE en una base anterior). El middleware la mapea a 409.
/// </summary>
public sealed class DocumentoDuplicadoException(string mensaje) : Exception(mensaje)
{
}
