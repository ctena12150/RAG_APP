-- Migración puntual para bases creadas antes de las columnas de identidad y roles.
-- Idempotente: se puede ejecutar varias veces sin efecto.
-- Uso: psql "$RAG_POSTGRES" -f scripts/postgres/migracion-identidades.sql
-- (El API .NET ya aplica esto solo al arrancar; este archivo es para desbloqueo manual.)

ALTER TABLE app.conversaciones ADD COLUMN IF NOT EXISTS usuario_id varchar(320);
ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS rol varchar(20) NOT NULL DEFAULT 'usuario';
ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS dominios text[] NOT NULL DEFAULT '{}';
CREATE INDEX IF NOT EXISTS ix_conversaciones_usuario ON app.conversaciones(usuario_id, actualizado_utc DESC);
