-- ─────────────────────────────────────────────────────────────────────────────
-- NARS — Complete database creation script
-- Creates the nars_db database and all objects from scratch.
--
-- Run as a PostgreSQL superuser:
--   psql -U postgres -f create_nars_db.sql
--
-- Or with password prompt:
--   PGPASSWORD=yourpassword psql -U postgres -f create_nars_db.sql
-- ─────────────────────────────────────────────────────────────────────────────

-- ══════════════════════════════════════════════════════════════════════════════
-- 1.  Database
-- ══════════════════════════════════════════════════════════════════════════════

-- ⚠ \gexec is a psql meta-command — this file must be run via psql, not
-- from application code (Npgsql, psycopg2). If run programmatically, replace
-- with a DO block executing the SELECT result dynamically.
SELECT 'CREATE DATABASE nars_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'nars_db') \gexec

\c nars_db

-- Enable PostGIS (required for communes_boundaries geometry column)
CREATE EXTENSION IF NOT EXISTS postgis;

-- ══════════════════════════════════════════════════════════════════════════════
-- 2.  EF Core migrations history
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory"
(
    "MigrationId"   character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- ══════════════════════════════════════════════════════════════════════════════
-- 3.  Reference tables (wilayas → dairas → communes)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.wilayas
(
    wilaya_id       integer NOT NULL,
    wilaya_ar       text,   -- intentional: no length limit for names/translations
    wilaya_fr       text,
    wilaya_latitude  double precision,   -- nullable: incomplete reference data allowed
    wilaya_longitude double precision,
    CONSTRAINT wilayas_pkey PRIMARY KEY (wilaya_id)
);

CREATE TABLE IF NOT EXISTS public.dairas
(
    daira_id        integer NOT NULL,
    wilaya_id       integer NOT NULL,
    daira_ar        character varying,
    daira_fr        character varying,
    daira_name      character varying,
    daira_latitude  double precision,   -- nullable
    daira_longitude double precision,
    CONSTRAINT dairas_pkey PRIMARY KEY (daira_id),
    CONSTRAINT dairas_wilaya_fk FOREIGN KEY (wilaya_id)
        REFERENCES public.wilayas (wilaya_id)
        ON UPDATE NO ACTION ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS public.communes
(
    commune_id        integer NOT NULL,
    daira_id          integer NOT NULL,
    commune_code      integer,           -- nullable
    commune_ar        character varying NOT NULL,
    commune_fr        character varying NOT NULL,
    commune_name      character varying,
    commune_latitude  double precision,  -- nullable
    commune_longitude double precision,
    CONSTRAINT communes_pkey PRIMARY KEY (commune_id),
    CONSTRAINT communes_daira_fk FOREIGN KEY (daira_id)
        REFERENCES public.dairas (daira_id)
        ON UPDATE NO ACTION ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS public.communes_boundaries
(
    commune_id integer NOT NULL,
    geometry   geometry NOT NULL,
    CONSTRAINT communes_boundaries_pkey PRIMARY KEY (commune_id),
    CONSTRAINT communes_boundaries_commune_fk FOREIGN KEY (commune_id)
        REFERENCES public.communes (commune_id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Spatial index — required for ST_Intersects / ST_Covers performance
CREATE INDEX IF NOT EXISTS ix_communes_boundaries_geometry
    ON public.communes_boundaries USING GIST (geometry);

-- ══════════════════════════════════════════════════════════════════════════════
-- 4.  Users (with role-based access control)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.users
(
    id                    uuid                     NOT NULL,
    name                  character varying(255)   NOT NULL,
    email                 character varying(255)   NOT NULL,
    phone                 character varying(50)    NOT NULL,
    username              character varying(100)   NOT NULL,
    password_hash         character varying(255)   NOT NULL,
    -- Role hierarchy: commune_user → daira_admin → wilaya_admin → national_admin
    -- national_admin is inserted directly into the database (no API endpoint).
    role                  character varying(20)    NOT NULL DEFAULT 'commune_user',
    -- Geographic scope — exactly one of these is set depending on role:
    --   commune_user   → commune_id
    --   daira_admin    → daira_id
    --   wilaya_admin   → wilaya_id
    --   national_admin → all NULL
    commune_id            integer,
    daira_id              integer,
    wilaya_id             integer,
    created_at            timestamp with time zone NOT NULL DEFAULT now(),
    failed_login_attempts integer NOT NULL DEFAULT 0,
    locked_until          timestamp with time zone,
    CONSTRAINT users_pkey        PRIMARY KEY (id),
    CONSTRAINT users_email_key   UNIQUE (email),
    CONSTRAINT users_username_key UNIQUE (username),
    -- Enforce that each role carries the correct geographic anchor.
    CONSTRAINT chk_user_geographic_role CHECK (
        CASE role
            WHEN 'commune_user'   THEN commune_id IS NOT NULL
            WHEN 'field_worker'   THEN commune_id IS NOT NULL
            WHEN 'daira_admin'    THEN daira_id   IS NOT NULL
            WHEN 'wilaya_admin'   THEN wilaya_id  IS NOT NULL
            WHEN 'national_admin' THEN TRUE
            ELSE FALSE
        END
    ),
    CONSTRAINT users_commune_fk FOREIGN KEY (commune_id)
        REFERENCES public.communes (commune_id)
        ON UPDATE NO ACTION ON DELETE SET NULL,
    CONSTRAINT users_daira_fk FOREIGN KEY (daira_id)
        REFERENCES public.dairas (daira_id)
        ON UPDATE NO ACTION ON DELETE SET NULL,
    CONSTRAINT users_wilaya_fk FOREIGN KEY (wilaya_id)
        REFERENCES public.wilayas (wilaya_id)
        ON UPDATE NO ACTION ON DELETE SET NULL
);

-- Indices for admin dashboard and per-role queries
CREATE INDEX IF NOT EXISTS ix_users_role
    ON public.users (role);
CREATE INDEX IF NOT EXISTS ix_users_commune_role
    ON public.users (commune_id, role)
    WHERE commune_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_daira_id
    ON public.users (daira_id)
    WHERE daira_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_wilaya_id
    ON public.users (wilaya_id)
    WHERE wilaya_id IS NOT NULL;

-- ══════════════════════════════════════════════════════════════════════════════
-- 5.  Authentication — refresh tokens
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.refresh_tokens
(
    id          uuid                     NOT NULL,
    user_id     uuid                     NOT NULL,
    token_hash  text                     NOT NULL,  -- SHA-256 hex (64 chars); use TEXT to avoid overflow if algorithm changes
    expires_at  timestamp with time zone NOT NULL,
    created_at  timestamp with time zone NOT NULL DEFAULT now(),
    revoked     boolean                  NOT NULL DEFAULT false,
    CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id),
    CONSTRAINT FK_refresh_tokens_users_user_id FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_token_hash
    ON public.refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id
    ON public.refresh_tokens (user_id);

-- ══════════════════════════════════════════════════════════════════════════════
-- 6.  Feature registry (cross-type UUID index)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.feature_registry
(
    id           uuid                   NOT NULL,
    feature_type character varying(30)  NOT NULL,
    CONSTRAINT feature_registry_pkey PRIMARY KEY (id)
);

-- ══════════════════════════════════════════════════════════════════════════════
-- 7.  Feature tables (all share the same base schema)
-- ══════════════════════════════════════════════════════════════════════════════

-- ── Areas ─────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.areas
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL,
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT areas_pkey PRIMARY KEY (id),
    CONSTRAINT areas_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_areas_user_id    ON public.areas (user_id);
CREATE INDEX IF NOT EXISTS ix_areas_user_layer ON public.areas (user_id, layer);

-- ── Districts ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.districts
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL,
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT districts_pkey PRIMARY KEY (id),
    CONSTRAINT districts_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_districts_user_id ON public.districts (user_id);

-- ── City centres ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.city_centers
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL DEFAULT 'city_center',
    label      character varying(500)   NOT NULL DEFAULT 'City Center',
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT city_centers_pkey PRIMARY KEY (id),
    CONSTRAINT city_centers_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_city_centers_user_id ON public.city_centers (user_id);

-- ── Roads ─────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.roads
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL,
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT roads_pkey PRIMARY KEY (id),
    CONSTRAINT roads_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_roads_user_id    ON public.roads (user_id);
CREATE INDEX IF NOT EXISTS ix_roads_user_layer ON public.roads (user_id, layer);

-- ── House entrances ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.house_entrances
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    road_id    uuid,
    layer      character varying(50)    NOT NULL,
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT house_entrances_pkey PRIMARY KEY (id),
    CONSTRAINT house_entrances_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE,
    CONSTRAINT house_entrances_road_fk FOREIGN KEY (road_id)
        REFERENCES public.roads (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_house_entrances_user_id    ON public.house_entrances (user_id);
CREATE INDEX IF NOT EXISTS ix_house_entrances_road_id    ON public.house_entrances (road_id);
CREATE INDEX IF NOT EXISTS ix_house_entrances_user_layer ON public.house_entrances (user_id, layer);

-- ── Public buildings ──────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.public_buildings
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL DEFAULT 'public_building',
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT public_buildings_pkey PRIMARY KEY (id),
    CONSTRAINT public_buildings_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_public_buildings_user_id ON public.public_buildings (user_id);

-- ── Public spaces ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.public_spaces
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL,
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT public_spaces_pkey PRIMARY KEY (id),
    CONSTRAINT public_spaces_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_public_spaces_user_id ON public.public_spaces (user_id);

-- ── Naming panels ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.naming_panels
(
    id         uuid                     NOT NULL,
    user_id    uuid,
    layer      character varying(50)    NOT NULL DEFAULT 'naming_panel',
    label      character varying(500)   NOT NULL,
    data       jsonb                    NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    CONSTRAINT naming_panels_pkey PRIMARY KEY (id),
    CONSTRAINT naming_panels_user_fk FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_naming_panels_user_id ON public.naming_panels (user_id);

-- ── Inspections (field-worker inspections) ────────────────────────────────────
-- Schema mirrors migration 20260511062948_AddInspections (timestamptz per
-- 20260705061915_MigrateToTimestamptz) plus the user FK added by
-- 20260808070428_AddForeignKeys. FK names match the EF model so a fresh
-- database initialized from this script converges with a migrated one
-- (no duplicate constraints).
CREATE TABLE IF NOT EXISTS public.inspections
(
    id         uuid                     NOT NULL,
    feature_id uuid                     NOT NULL,
    user_id    uuid                     NOT NULL,
    type       character varying(30)    NOT NULL,
    data       jsonb                    NOT NULL,
    status     character varying(20)    NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT inspections_pkey PRIMARY KEY (id),
    CONSTRAINT FK_inspections_users_user_id FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_inspections_feature_id
    ON public.inspections (feature_id);
CREATE INDEX IF NOT EXISTS ix_inspections_user_id
    ON public.inspections (user_id);
-- Optimizes "latest inspections per feature" queries (feature_id ASC, created_at DESC)
CREATE INDEX IF NOT EXISTS ix_inspections_feature_created
    ON public.inspections (feature_id, created_at DESC);

-- ══════════════════════════════════════════════════════════════════════════════
-- 9.  Error logs (written by NarsApi.LogsController on unhandled exceptions)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS public.error_logs
(
    id          uuid                     NOT NULL,
    user_id     uuid,
    level       character varying(20)    NOT NULL,
    code        character varying(50)    NOT NULL,
    message     text                     NOT NULL,
    context     text,
    url         character varying(2048),
    method      character varying(10),
    ip_address  character varying(45),
    user_agent  character varying(500),
    created_at  timestamp with time zone NOT NULL,
    CONSTRAINT error_logs_pkey PRIMARY KEY (id),
    CONSTRAINT FK_error_logs_users_user_id FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

-- ══════════════════════════════════════════════════════════════════════════════
-- 10.  Indices for error_logs (admin dashboard queries)
-- ══════════════════════════════════════════════════════════════════════════════

CREATE INDEX IF NOT EXISTS ix_error_logs_created_at ON public.error_logs (created_at);
CREATE INDEX IF NOT EXISTS ix_error_logs_user_id    ON public.error_logs (user_id);
CREATE INDEX IF NOT EXISTS ix_error_logs_level      ON public.error_logs (level);

-- ══════════════════════════════════════════════════════════════════════════════
-- Verification
-- ══════════════════════════════════════════════════════════════════════════════
DO $$
DECLARE
    tbl_count  integer;
    idx_count  integer;
BEGIN
    SELECT COUNT(*) INTO tbl_count
    FROM information_schema.tables
    WHERE table_schema = 'public'
      AND table_type   = 'BASE TABLE';

    SELECT COUNT(*) INTO idx_count
    FROM pg_indexes
    WHERE schemaname = 'public'
      AND indexname NOT LIKE '%_pkey%';

    RAISE NOTICE '──────────────────────────────────────────';
    RAISE NOTICE 'NARS database created successfully.';
    RAISE NOTICE '  Tables  : %', tbl_count;
    RAISE NOTICE '  Indices : %', idx_count;
    RAISE NOTICE '──────────────────────────────────────────';
    RAISE NOTICE 'Next step: run create_national_admin.sh';
    RAISE NOTICE '──────────────────────────────────────────';
END $$;
