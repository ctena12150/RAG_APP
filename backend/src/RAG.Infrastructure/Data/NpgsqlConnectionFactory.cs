using Npgsql;
using RAG.Domain.Interfaces;

namespace RAG.Infrastructure.Data;

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
