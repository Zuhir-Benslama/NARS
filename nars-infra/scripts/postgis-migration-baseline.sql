CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $$
DECLARE
    has_areas boolean;
    has_inspections boolean;
    has_inspections_fk boolean;
    has_security_stamp boolean;
    has_roundabouts boolean;
    has_city_centers boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'areas'
    ) INTO has_areas;

    SELECT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'inspections'
    ) INTO has_inspections;

    -- AddForeignKeys (20260808070428) is reflected by the inspections→users
    -- FK that both the EF migration and create_nars_db.sql create.
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE table_schema = 'public'
          AND constraint_type = 'FOREIGN KEY'
          AND table_name = 'inspections'
          AND UPPER(constraint_name) = 'FK_INSPECTIONS_USERS_USER_ID'
    ) INTO has_inspections_fk;

    -- AddUserSecurityStamp (20260810062821) is reflected by the column itself.
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'users'
          AND column_name = 'security_stamp'
    ) INTO has_security_stamp;

    -- RenameCityCenterToRoundabouts is already reflected by the table name in
    -- schemas created from create_nars_db.sql. Older schemas still have
    -- `city_centers` and must run the EF migration to preserve their rows.
    -- Do not baseline when both tables exist: that needs manual inspection so
    -- no feature rows are silently orphaned or overwritten.
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'roundabouts'
    ) INTO has_roundabouts;

    SELECT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'city_centers'
    ) INTO has_city_centers;

    IF has_areas THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260510191030_AddErrorLogs', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;

    IF has_inspections THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260511062948_AddInspections', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;

    IF has_areas AND has_inspections THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260705061915_MigrateToTimestamptz', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;

    IF has_inspections_fk THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260808070428_AddForeignKeys', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;

    IF has_security_stamp THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20260810062821_AddUserSecurityStamp', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;

    IF has_roundabouts AND NOT has_city_centers THEN
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20261127080208_RenameCityCenterToRoundabouts', '10.0.10')
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;
END $$;
