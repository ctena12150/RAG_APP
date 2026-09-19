-- Esquema RAG App — PostgreSQL 16+ con extensión pgvector
-- El backend .NET crea y posee el esquema "app"; el servicio Python crea y posee el esquema "rag"
-- (chunks + embeddings pgvector + índice full-text 'spanish'). Este script solo cubre "app";
-- el esquema "rag" se auto-crea al arrancar rag-service (app.db.ensure_schema).
--
-- Requisitos: CREATE EXTENSION vector;  (lo ejecuta también Python de forma idempotente)

CREATE SCHEMA IF NOT EXISTS app;

-- Dominios gestionables por el superusuario (clave inmutable, etiqueta y descripción editables).
-- Los documentos/carpetas referencian por clave sin FK: el borrado se bloquea en API si está en uso.
CREATE TABLE IF NOT EXISTS app.dominios (
    clave       varchar(30)  PRIMARY KEY CHECK (clave ~ '^[a-z0-9-]{2,30}$'),
    etiqueta    varchar(100) NOT NULL,
    descripcion varchar(300) NOT NULL DEFAULT '',
    creado_utc  timestamptz  NOT NULL DEFAULT now()
);

INSERT INTO app.dominios (clave, etiqueta, descripcion) VALUES
    ('rrhh', 'Recursos Humanos', 'Nóminas, vacaciones, beneficios, políticas de personal'),
    ('mantenimiento', 'Mantenimiento', 'Manuales técnicos, procedimientos de equipos, calibraciones'),
    ('onboarding', 'Onboarding', 'Alta de empleados, checklist, formación inicial'),
    ('it', 'IT', 'Sistemas, accesos, incidencias y soporte tecnológico')
ON CONFLICT (clave) DO NOTHING;

CREATE TABLE IF NOT EXISTS app.carpetas (
    id          uuid PRIMARY KEY,
    nombre      varchar(100) NOT NULL,
    dominio     varchar(30)  NOT NULL,
    creado_utc  timestamptz  NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS app.documentos (
    id             uuid PRIMARY KEY,
    nombre_archivo varchar(260) NOT NULL,
    dominio        varchar(30)  NOT NULL,
    folder_id      uuid REFERENCES app.carpetas(id) ON DELETE SET NULL,
    tamano_bytes   bigint       NOT NULL,
    content_hash   char(64)     NOT NULL,
    estado         smallint     NOT NULL DEFAULT 0 CHECK (estado BETWEEN 0 AND 3), -- pendiente|procesando|listo|error
    error_mensaje  varchar(1000),
    total_paginas  integer,
    creado_utc     timestamptz  NOT NULL DEFAULT now(),
    procesado_utc  timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_documentos_content_hash ON app.documentos(content_hash);
CREATE INDEX IF NOT EXISTS ix_documentos_listado ON app.documentos(dominio, estado, creado_utc DESC);
CREATE INDEX IF NOT EXISTS ix_documentos_folder ON app.documentos(folder_id);

CREATE TABLE IF NOT EXISTS app.conversaciones (
    id                  uuid PRIMARY KEY,
    titulo              varchar(200) NOT NULL,
    titulo_automatico   boolean      NOT NULL DEFAULT true,
    dominios_json       jsonb,
    documentos_ids_json jsonb,
    usuario_id          varchar(320),
    creado_utc          timestamptz  NOT NULL DEFAULT now(),
    actualizado_utc     timestamptz  NOT NULL DEFAULT now()
);

-- migraciones para bases creadas con versiones anteriores del script (bases nuevas
-- ya traen las columnas). Van ANTES de los índices que las usan para que el archivo
-- sea re-ejecutable de principio a fin en una base vieja.
ALTER TABLE app.conversaciones ADD COLUMN IF NOT EXISTS usuario_id varchar(320);
ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS rol varchar(20) NOT NULL DEFAULT 'usuario';
ALTER TABLE app.usuarios ADD COLUMN IF NOT EXISTS dominios text[] NOT NULL DEFAULT '{}';
ALTER TABLE app.carpetas ALTER COLUMN dominio TYPE varchar(30);
ALTER TABLE app.documentos ALTER COLUMN dominio TYPE varchar(30);
ALTER TABLE app.carpetas DROP CONSTRAINT IF EXISTS carpetas_dominio_check;
ALTER TABLE app.documentos DROP CONSTRAINT IF EXISTS documentos_dominio_check;

CREATE INDEX IF NOT EXISTS ix_conversaciones_usuario ON app.conversaciones(usuario_id, actualizado_utc DESC);

CREATE TABLE IF NOT EXISTS app.mensajes (
    id                 uuid PRIMARY KEY,
    conversacion_id    uuid NOT NULL REFERENCES app.conversaciones(id) ON DELETE CASCADE,
    rol                varchar(12) NOT NULL CHECK (rol IN ('user', 'assistant')),
    contenido          text        NOT NULL,
    fuentes_json       jsonb,
    traza_json         jsonb,
    verificacion_json  jsonb,
    metricas_json      jsonb,
    revision_contenido text,
    creado_utc         timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_mensajes_conversacion ON app.mensajes(conversacion_id, creado_utc);

ALTER TABLE app.mensajes ADD COLUMN IF NOT EXISTS metricas_json jsonb;

-- Lista blanca de acceso al chat (login Google/Microsoft): una entrada es un email
-- exacto O un dominio (parte tras la @). Nunca ambos.
CREATE TABLE IF NOT EXISTS app.usuarios_permitidos (
    id         uuid PRIMARY KEY,
    email      varchar(320),
    dominio    varchar(253),
    activo     boolean      NOT NULL DEFAULT true,
    creado_utc timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT chk_email_o_dominio
        CHECK ((email IS NOT NULL AND dominio IS NULL) OR (email IS NULL AND dominio IS NOT NULL)),
    CONSTRAINT chk_email_contiene_arroba
        CHECK (email IS NULL OR email LIKE '%@%'),
    CONSTRAINT chk_dominio_sin_arroba
        CHECK (dominio IS NULL OR dominio NOT LIKE '%@%')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_permitidos_email ON app.usuarios_permitidos(email) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_permitidos_dominio ON app.usuarios_permitidos(dominio) WHERE dominio IS NOT NULL;

-- Credenciales locales de acceso al chat (login usuario/contraseña). La contraseña viaja
-- siempre como hash BCrypt (password_hash); el nombre de usuario es único e ignorará mayúsculas.
-- El rol (usuario|teamleader|superusuario) controla subida de documentos y administración.
CREATE TABLE IF NOT EXISTS app.usuarios (
    id            uuid PRIMARY KEY,
    usuario       varchar(100) NOT NULL,
    password_hash varchar(255) NOT NULL,
    email         varchar(320),
    nombre        varchar(200),
    rol           varchar(20)  NOT NULL DEFAULT 'usuario',
    dominios      text[]       NOT NULL DEFAULT '{}',
    activo        boolean      NOT NULL DEFAULT true,
    creado_utc    timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_usuario ON app.usuarios(lower(usuario));
