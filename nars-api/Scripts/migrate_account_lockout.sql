-- Migration: Add account lockout columns to users table
-- Date: 2026-04-04
--
-- Run: psql -h localhost -U postgres -d nars_db -f migrate_account_lockout.sql

BEGIN;

ALTER TABLE users ADD COLUMN IF NOT EXISTS failed_login_attempts  INT          DEFAULT 0;
ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_until           TIMESTAMPTZ  DEFAULT NULL;

COMMIT;
