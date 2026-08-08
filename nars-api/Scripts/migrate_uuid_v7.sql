-- Migration: Integer IDs → UUID v7
-- Date: 2026-04-04
--
-- Prerequisite: pgcrypto extension (for gen_random_bytes)
--   CREATE EXTENSION IF NOT EXISTS pgcrypto;
--
-- Run: psql -h localhost -U postgres -d nars_db -f migrate_uuid_v7.sql
-- BACKUP FIRST: ./backup-db.sh

BEGIN;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 0: Ensure pgcrypto extension exists
-- ──────────────────────────────────────────────────────────────────────────────

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 1: UUID v7 generation function
-- ──────────────────────────────────────────────────────────────────────────────
-- UUID v7 = 48-bit Unix timestamp (ms) + 74 bits randomness with version/variant
-- Uses clock_timestamp() so each call gets a monotonically increasing timestamp.

CREATE OR REPLACE FUNCTION uuid_v7()
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    ts     bigint;
    rand   bytea;
    result text;
BEGIN
    ts := floor(extract(epoch from clock_timestamp()) * 1000)::bigint;
    rand := gen_random_bytes(10);

    result :=
        lpad(to_hex(ts), 12, '0') ||
        lpad(to_hex((get_byte(rand, 0) & 15) | 112), 2, '0') ||
        lpad(to_hex(get_byte(rand, 1)), 2, '0') ||
        lpad(to_hex((get_byte(rand, 2) & 63) | 128), 2, '0') ||
        lpad(to_hex(get_byte(rand, 3)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 4)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 5)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 6)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 7)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 8)), 2, '0') ||
        lpad(to_hex(get_byte(rand, 9)), 2, '0');

    RETURN (
        substring(result, 1, 8) || '-' ||
        substring(result, 9, 4) || '-' ||
        substring(result, 13, 4) || '-' ||
        substring(result, 17, 4) || '-' ||
        substring(result, 21, 12)
    )::uuid;
END;
$$;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 1: Drop existing FK constraints and the v_features view
-- ──────────────────────────────────────────────────────────────────────────────

ALTER TABLE areas            DROP CONSTRAINT IF EXISTS areas_user_fk;
ALTER TABLE city_centers     DROP CONSTRAINT IF EXISTS city_centers_user_fk;
ALTER TABLE districts        DROP CONSTRAINT IF EXISTS districts_user_fk;
ALTER TABLE house_entrances  DROP CONSTRAINT IF EXISTS house_entrances_user_fk;
ALTER TABLE house_entrances  DROP CONSTRAINT IF EXISTS house_entrances_road_fk;
ALTER TABLE naming_panels    DROP CONSTRAINT IF EXISTS naming_panels_user_fk;
ALTER TABLE public_buildings DROP CONSTRAINT IF EXISTS public_buildings_user_fk;
ALTER TABLE public_spaces    DROP CONSTRAINT IF EXISTS public_spaces_user_fk;
ALTER TABLE roads            DROP CONSTRAINT IF EXISTS roads_user_fk;
ALTER TABLE refresh_tokens   DROP CONSTRAINT IF EXISTS refresh_tokens_user_id_fkey;

-- Drop the v_features view — it depends on all feature table id columns.
-- It will be recreated at the end with updated column types.
DROP VIEW IF EXISTS v_features;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 3: Add new UUID columns (PK + FK)
-- ──────────────────────────────────────────────────────────────────────────────

ALTER TABLE users            ADD COLUMN new_id uuid;
ALTER TABLE areas            ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE districts        ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE city_centers     ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE roads            ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE house_entrances  ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid, ADD COLUMN new_road_id uuid;
ALTER TABLE public_buildings ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE public_spaces    ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE naming_panels    ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;
ALTER TABLE feature_registry ADD COLUMN new_id uuid;
ALTER TABLE refresh_tokens   ADD COLUMN new_id uuid, ADD COLUMN new_user_id uuid;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 4: Populate PK uuids (clock_timestamp ensures monotonic ordering)
-- ──────────────────────────────────────────────────────────────────────────────

UPDATE users            SET new_id = uuid_v7();
UPDATE areas            SET new_id = uuid_v7();
UPDATE districts        SET new_id = uuid_v7();
UPDATE city_centers     SET new_id = uuid_v7();
UPDATE roads            SET new_id = uuid_v7();
UPDATE house_entrances  SET new_id = uuid_v7();
UPDATE public_buildings SET new_id = uuid_v7();
UPDATE public_spaces    SET new_id = uuid_v7();
UPDATE naming_panels    SET new_id = uuid_v7();
UPDATE feature_registry SET new_id = uuid_v7();
UPDATE refresh_tokens   SET new_id = uuid_v7();

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 5: Populate FK uuid columns by JOINing on old int IDs
-- ──────────────────────────────────────────────────────────────────────────────

UPDATE areas            SET new_user_id = u.new_id FROM users u WHERE areas.user_id = u.id;
UPDATE districts        SET new_user_id = u.new_id FROM users u WHERE districts.user_id = u.id;
UPDATE city_centers     SET new_user_id = u.new_id FROM users u WHERE city_centers.user_id = u.id;
UPDATE roads            SET new_user_id = u.new_id FROM users u WHERE roads.user_id = u.id;
UPDATE house_entrances  SET new_user_id = u.new_id FROM users u WHERE house_entrances.user_id = u.id;
UPDATE house_entrances  SET new_road_id = r.new_id FROM roads r WHERE house_entrances.road_id = r.id;
UPDATE public_buildings SET new_user_id = u.new_id FROM users u WHERE public_buildings.user_id = u.id;
UPDATE public_spaces    SET new_user_id = u.new_id FROM users u WHERE public_spaces.user_id = u.id;
UPDATE naming_panels    SET new_user_id = u.new_id FROM users u WHERE naming_panels.user_id = u.id;
UPDATE refresh_tokens   SET new_user_id = u.new_id FROM users u WHERE refresh_tokens.user_id = u.id;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 6: Swap columns — drop old, rename new
-- ──────────────────────────────────────────────────────────────────────────────

-- users
ALTER TABLE users DROP CONSTRAINT users_pkey;
ALTER TABLE users DROP COLUMN id;
ALTER TABLE users RENAME COLUMN new_id TO id;
ALTER TABLE users ALTER COLUMN id SET NOT NULL;
ALTER TABLE users ADD CONSTRAINT users_pkey PRIMARY KEY (id);

-- Feature tables with user_id FK (loop)
DO $$
DECLARE tbl text;
BEGIN
    FOR tbl IN
        SELECT unnest(ARRAY[
            'areas', 'districts', 'city_centers', 'roads',
            'house_entrances', 'public_buildings', 'public_spaces', 'naming_panels'
        ])
    LOOP
        EXECUTE format('ALTER TABLE %I DROP CONSTRAINT IF EXISTS %I_pkey', tbl, tbl);
        EXECUTE format('ALTER TABLE %I DROP COLUMN id', tbl);
        EXECUTE format('ALTER TABLE %I RENAME COLUMN new_id TO id', tbl);
        EXECUTE format('ALTER TABLE %I ALTER COLUMN id SET NOT NULL', tbl);
        EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I_pkey PRIMARY KEY (id)', tbl, tbl);
        EXECUTE format('ALTER TABLE %I DROP COLUMN user_id', tbl);
        EXECUTE format('ALTER TABLE %I RENAME COLUMN new_user_id TO user_id', tbl);
    END LOOP;
END $$;

-- feature_registry: only has id (no user_id)
ALTER TABLE feature_registry DROP CONSTRAINT IF EXISTS feature_registry_pkey;
ALTER TABLE feature_registry DROP COLUMN id;
ALTER TABLE feature_registry RENAME COLUMN new_id TO id;
ALTER TABLE feature_registry ALTER COLUMN id SET NOT NULL;
ALTER TABLE feature_registry ADD CONSTRAINT feature_registry_pkey PRIMARY KEY (id);

-- house_entrances: also handle road_id FK
ALTER TABLE house_entrances DROP COLUMN road_id;
ALTER TABLE house_entrances RENAME COLUMN new_road_id TO road_id;

-- refresh_tokens
ALTER TABLE refresh_tokens DROP CONSTRAINT refresh_tokens_pkey;
ALTER TABLE refresh_tokens DROP COLUMN id;
ALTER TABLE refresh_tokens RENAME COLUMN new_id TO id;
ALTER TABLE refresh_tokens ALTER COLUMN id SET NOT NULL;
ALTER TABLE refresh_tokens ADD CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id);
ALTER TABLE refresh_tokens DROP COLUMN user_id;
ALTER TABLE refresh_tokens RENAME COLUMN new_user_id TO user_id;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 7: Drop old sequences
-- ──────────────────────────────────────────────────────────────────────────────

DROP SEQUENCE IF EXISTS feature_id_seq;
DROP SEQUENCE IF EXISTS users_id_seq;
DROP SEQUENCE IF EXISTS refresh_tokens_id_seq;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 8: Recreate FK constraints
-- ──────────────────────────────────────────────────────────────────────────────

ALTER TABLE areas            ADD CONSTRAINT areas_user_fk            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE districts        ADD CONSTRAINT districts_user_fk        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE city_centers     ADD CONSTRAINT city_centers_user_fk     FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE roads            ADD CONSTRAINT roads_user_fk            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE house_entrances  ADD CONSTRAINT house_entrances_user_fk  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE house_entrances  ADD CONSTRAINT house_entrances_road_fk  FOREIGN KEY (road_id)   REFERENCES roads(id) ON DELETE CASCADE;
ALTER TABLE public_buildings ADD CONSTRAINT public_buildings_user_fk FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE public_spaces    ADD CONSTRAINT public_spaces_user_fk    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE naming_panels    ADD CONSTRAINT naming_panels_user_fk    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE refresh_tokens   ADD CONSTRAINT FK_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 9: Recreate indexes
-- ──────────────────────────────────────────────────────────────────────────────

DROP INDEX IF EXISTS ix_areas_user_id;
DROP INDEX IF EXISTS ix_areas_user_layer;
DROP INDEX IF EXISTS ix_districts_user_id;
DROP INDEX IF EXISTS ix_city_centers_user_id;
DROP INDEX IF EXISTS ix_roads_user_id;
DROP INDEX IF EXISTS ix_roads_user_layer;
DROP INDEX IF EXISTS ix_house_entrances_user_id;
DROP INDEX IF EXISTS ix_house_entrances_user_layer;
DROP INDEX IF EXISTS ix_house_entrances_road_id;
DROP INDEX IF EXISTS ix_public_buildings_user_id;
DROP INDEX IF EXISTS ix_public_spaces_user_id;
DROP INDEX IF EXISTS ix_naming_panels_user_id;
DROP INDEX IF EXISTS idx_refresh_tokens_user_id;

CREATE INDEX ix_areas_user_id             ON areas(user_id);
CREATE INDEX ix_areas_user_layer          ON areas(user_id, layer);
CREATE INDEX ix_districts_user_id         ON districts(user_id);
CREATE INDEX ix_city_centers_user_id      ON city_centers(user_id);
CREATE INDEX ix_roads_user_id             ON roads(user_id);
CREATE INDEX ix_roads_user_layer          ON roads(user_id, layer);
CREATE INDEX ix_house_entrances_user_id   ON house_entrances(user_id);
CREATE INDEX ix_house_entrances_user_layer ON house_entrances(user_id, layer);
CREATE INDEX ix_house_entrances_road_id   ON house_entrances(road_id) WHERE road_id IS NOT NULL;
CREATE INDEX ix_public_buildings_user_id  ON public_buildings(user_id);
CREATE INDEX ix_public_spaces_user_id     ON public_spaces(user_id);
CREATE INDEX ix_naming_panels_user_id     ON naming_panels(user_id);
CREATE INDEX idx_refresh_tokens_user_id   ON refresh_tokens(user_id);

-- ──────────────────────────────────────────────────────────────────────────────
-- Step 10: Recreate v_features view
-- ──────────────────────────────────────────────────────────────────────────────

CREATE VIEW v_features AS
 SELECT areas.id, areas.user_id, 'area'::text AS type, areas.layer, areas.label, areas.data, areas.created_at, areas.updated_at FROM areas
UNION ALL SELECT districts.id, districts.user_id, 'district'::text, districts.layer, districts.label, districts.data, districts.created_at, districts.updated_at FROM districts
UNION ALL SELECT city_centers.id, city_centers.user_id, 'city_center'::text, city_centers.layer, city_centers.label, city_centers.data, city_centers.created_at, city_centers.updated_at FROM city_centers
UNION ALL SELECT roads.id, roads.user_id, 'road'::text, roads.layer, roads.label, roads.data, roads.created_at, roads.updated_at FROM roads
UNION ALL SELECT house_entrances.id, house_entrances.user_id, 'house_entrance'::text, house_entrances.layer, house_entrances.label, house_entrances.data, house_entrances.created_at, house_entrances.updated_at FROM house_entrances
UNION ALL SELECT public_buildings.id, public_buildings.user_id, 'public_building'::text, public_buildings.layer, public_buildings.label, public_buildings.data, public_buildings.created_at, public_buildings.updated_at FROM public_buildings
UNION ALL SELECT public_spaces.id, public_spaces.user_id, 'public_space'::text, public_spaces.layer, public_spaces.label, public_spaces.data, public_spaces.created_at, public_spaces.updated_at FROM public_spaces
UNION ALL SELECT naming_panels.id, naming_panels.user_id, 'naming_panel'::text, naming_panels.layer, naming_panels.label, naming_panels.data, naming_panels.created_at, naming_panels.updated_at FROM naming_panels;

COMMIT;
