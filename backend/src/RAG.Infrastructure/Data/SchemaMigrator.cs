using Dapper;

namespace RAG.Infrastructure.Data;

/// <summary>
/// Migraciones idempotentes del esquema "app" para bases creadas con versiones
/// anteriores de schema.sql (el init de docker solo corre con volumen vacío).
/// Se ejecuta al arrancar el API con proveedor PostgreSql.
/// </summary>
public sealed class SchemaMigrator(IDbConnectionFactory factory)
{
    private const string Sql = """
        ALTER TABLE app.conversaciones ADD COLUMN IF NOT EXISTS usuario_id varchar(320);
        ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS rol varchar(20) NOT NULL DEFAULT 'usuario';
        ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS dominios text[] NOT NULL DEFAULT '{}';
        CREATE INDEX IF NOT EXISTS ix_conversaciones_usuario ON app.conversaciones(usuario_id, actualizado_utc DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_documentos_content_hash ON app.documentos(content_hash);
        """;

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(Sql, cancellationToken: ct));
    }
}
