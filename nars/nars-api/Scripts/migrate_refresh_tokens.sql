-- Migration: Add refresh_tokens table for JWT refresh token rotation
-- Date: 2026-04-04
--
-- Run: psql -h localhost -U postgres -d nars_db -f migrate_refresh_tokens.sql

BEGIN;

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id          BIGSERIAL   PRIMARY KEY,
    user_id     INT         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash  VARCHAR(64) NOT NULL,
    expires_at  TIMESTAMPTZ NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked     BOOLEAN     NOT NULL DEFAULT FALSE
);

CREATE INDEX idx_refresh_tokens_user_id     ON refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_token_hash  ON refresh_tokens(token_hash) WHERE NOT revoked;
CREATE INDEX idx_refresh_tokens_expires_at  ON refresh_tokens(expires_at);

COMMIT;
