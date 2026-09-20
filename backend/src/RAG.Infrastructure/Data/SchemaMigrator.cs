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
        CREATE TABLE IF NOT EXISTS app.dominios (
            clave varchar(30) PRIMARY KEY CHECK (clave ~ '^[a-z0-9-]{2,30}$'),
            etiqueta varchar(100) NOT NULL,
            descripcion varchar(300) NOT NULL DEFAULT '',
            ejemplos text[] NOT NULL DEFAULT '{}',
            creado_utc timestamptz NOT NULL DEFAULT now()
        );
        ALTER TABLE app.dominios ADD COLUMN IF NOT EXISTS ejemplos text[] NOT NULL DEFAULT '{}';
        INSERT INTO app.dominios (clave, etiqueta, descripcion, ejemplos) VALUES
            ('rrhh', 'Recursos Humanos', 'Nóminas, vacaciones, beneficios, políticas de personal',
             ARRAY['¿Cuántos días de vacaciones tengo?', '¿Cuándo se paga la nómina?', '¿Cómo solicito un permiso?']),
            ('mantenimiento', 'Mantenimiento', 'Manuales técnicos, procedimientos de equipos, calibraciones',
             ARRAY['¿Cada cuánto se revisa la caldera?', '¿Qué mantenimiento preventivo tiene la bomba?', '¿Cómo se calibra el sensor de presión?']),
            ('onboarding', 'Onboarding', 'Alta de empleados, checklist, formación inicial',
             ARRAY['¿Qué hago mi primer día?', '¿Dónde está el manual de bienvenida?', '¿Qué formación inicial es obligatoria?']),
            ('it', 'IT', 'Sistemas, accesos, incidencias y soporte tecnológico',
             ARRAY['No puedo acceder a la VPN, ¿qué hago?', '¿Cómo solicito un equipo nuevo?', '¿Cuál es la política de contraseñas?'])
        ON CONFLICT (clave) DO NOTHING;
        UPDATE app.dominios SET ejemplos = ARRAY['¿Cuántos días de vacaciones tengo?', '¿Cuándo se paga la nómina?', '¿Cómo solicito un permiso?'] WHERE clave = 'rrhh' AND ejemplos = '{}';
        UPDATE app.dominios SET ejemplos = ARRAY['¿Cada cuánto se revisa la caldera?', '¿Qué mantenimiento preventivo tiene la bomba?', '¿Cómo se calibra el sensor de presión?'] WHERE clave = 'mantenimiento' AND ejemplos = '{}';
        UPDATE app.dominios SET ejemplos = ARRAY['¿Qué hago mi primer día?', '¿Dónde está el manual de bienvenida?', '¿Qué formación inicial es obligatoria?'] WHERE clave = 'onboarding' AND ejemplos = '{}';
        UPDATE app.dominios SET ejemplos = ARRAY['No puedo acceder a la VPN, ¿qué hago?', '¿Cómo solicito un equipo nuevo?', '¿Cuál es la política de contraseñas?'] WHERE clave = 'it' AND ejemplos = '{}';
        ALTER TABLE app.carpetas ALTER COLUMN dominio TYPE varchar(30);
        ALTER TABLE app.documentos ALTER COLUMN dominio TYPE varchar(30);
        ALTER TABLE app.carpetas DROP CONSTRAINT IF EXISTS carpetas_dominio_check;
        ALTER TABLE app.documentos DROP CONSTRAINT IF EXISTS documentos_dominio_check;
        ALTER TABLE app.conversaciones ADD COLUMN IF NOT EXISTS usuario_id varchar(320);
        ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS rol varchar(20) NOT NULL DEFAULT 'usuario';
        ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS dominios text[] NOT NULL DEFAULT '{}';
        ALTER TABLE app.mensajes ADD COLUMN IF NOT EXISTS clarify_json jsonb;
        CREATE INDEX IF NOT EXISTS ix_conversaciones_usuario ON app.conversaciones(usuario_id, actualizado_utc DESC);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_documentos_content_hash ON app.documentos(content_hash);
        """;

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(Sql, cancellationToken: ct));
    }
}
