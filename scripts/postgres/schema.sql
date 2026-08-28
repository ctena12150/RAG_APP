-- Esquema RAG App — PostgreSQL 16+ con extensión pgvector
-- El backend .NET crea y posee el esquema "app"; el servicio Python crea y posee el esquema "rag"
-- (chunks + embeddings pgvector + índice full-text 'spanish'). Este script solo cubre "app";
-- el esquema "rag" se auto-crea al arrancar rag-service (app.db.ensure_schema).
--
-- Requisitos: CREATE EXTENSION vector;  (lo ejecuta también Python de forma idempotente)

CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE IF NOT EXISTS app.carpetas (
    id          uuid PRIMARY KEY,
    nombre      varchar(100) NOT NULL,
    dominio     varchar(20)  NOT NULL CHECK (dominio IN ('rrhh', 'mantenimiento', 'onboarding')),
    creado_utc  timestamptz  NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS app.documentos (
    id             uuid PRIMARY KEY,
    nombre_archivo varchar(260) NOT NULL,
    dominio        varchar(20)  NOT NULL CHECK (dominio IN ('rrhh', 'mantenimiento', 'onboarding')),
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
    creado_utc          timestamptz  NOT NULL DEFAULT now(),
    actualizado_utc     timestamptz  NOT NULL DEFAULT now()
);

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
