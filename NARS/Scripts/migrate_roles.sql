-- ─── NARS: Role-Based Access Control Migration ───────────────────────────────
-- Run once against the live DB. All changes are backward-compatible:
--   - Existing users keep commune_id and get role = 'commune_user' by default.
--   - commune_id is made nullable to accommodate admin accounts that are not
--     tied to a specific commune.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- 1. Make commune_id nullable — admins are not tied to a commune.
--    Existing NOT NULL rows are unaffected; the DEFAULT still applies on INSERT.
ALTER TABLE users ALTER COLUMN commune_id DROP NOT NULL;

-- 2. Role column — defaults to 'commune_user' so all existing accounts stay valid.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS role VARCHAR(20) NOT NULL DEFAULT 'commune_user';

-- 3. Geographic assignment columns for admin roles.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS daira_id  INTEGER REFERENCES dairas(daira_id),
    ADD COLUMN IF NOT EXISTS wilaya_id INTEGER REFERENCES wilayas(wilaya_id);

-- 4. Constraint: each role requires the appropriate geographic anchor.
--    commune_user  → commune_id must be set
--    daira_admin   → daira_id  must be set
--    wilaya_admin  → wilaya_id must be set
--    national_admin → no geographic restriction
ALTER TABLE users
    ADD CONSTRAINT chk_user_geographic_role CHECK (
        CASE role
            WHEN 'commune_user'   THEN commune_id IS NOT NULL
            WHEN 'daira_admin'    THEN daira_id   IS NOT NULL
            WHEN 'wilaya_admin'   THEN wilaya_id  IS NOT NULL
            WHEN 'national_admin' THEN TRUE
            ELSE FALSE   -- reject unknown roles at the DB level
        END
    );

-- 5. Indices for admin dashboard queries (join users by role + geographic id).
CREATE INDEX IF NOT EXISTS ix_users_role
    ON users(role);
CREATE INDEX IF NOT EXISTS ix_users_daira_id
    ON users(daira_id)  WHERE daira_id  IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_wilaya_id
    ON users(wilaya_id) WHERE wilaya_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_commune_role
    ON users(commune_id, role) WHERE commune_id IS NOT NULL;

COMMIT;
