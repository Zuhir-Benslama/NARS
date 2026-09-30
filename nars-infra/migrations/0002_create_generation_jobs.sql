-- Async road-generation jobs and their per-chunk work items.
--
-- A job captures one "generate roads from urban areas" run: the caller splits
-- an urban bounding box (UI side) into an 18-zoom tile grid, uploads each
-- rendered chunk as a raster, and nars-api processes the chunks on a worker
-- pool (generation_job_chunks -> DraftFeaturesService.SegmentTileAsync, which
-- delegates to nars-segma). Once every chunk is done the worker runs the
-- roads-phase acceptance (RoadGenerationService.GenerateAsync) and stores the
-- GenerateRoadsResponse JSON in generation_jobs.result.
--
-- Chunk rasters live here as BYTEA so jobs survive pod restarts: the client
-- only ever interacts through the job API and does not need the image again
-- (the queue is acked by the worker row transitions). Concurrency is handled
-- with FOR UPDATE SKIP LOCKED claims plus a heartbeat_at stamped by a worker;
-- a "running" chunk whose heartbeat has gone stale is reclaimable by any
-- worker (crashed-worker recovery). attempt counts cap how often a chunk is
-- retried per job.
--
-- ⚠ NAMES MUST STAY IN SYNC with section 11 of
-- nars-infra/scripts/create_nars_db.sql, which creates the same tables at
-- Docker image init. Both files are applied to the same databases
-- (init script → fresh clusters, `make db-migrate-nars` → any cluster) and
-- are idempotent BY NAME: divergent index/constraint names silently create
-- duplicates instead of no-oping.

CREATE TABLE IF NOT EXISTS generation_jobs (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    commune_id           INTEGER NOT NULL,
    created_by           UUID NOT NULL,
    status               VARCHAR(20) NOT NULL DEFAULT 'pending'
                             CONSTRAINT chk_generation_job_status
                             CHECK (status IN ('pending', 'active', 'accepting', 'done', 'failed', 'cancelled')),
    stage                VARCHAR(10)
                             CONSTRAINT chk_generation_job_stage
                             CHECK (stage IS NULL OR stage IN ('segment', 'accept')),
    total_chunks         INTEGER NOT NULL,
    done_chunks          INTEGER NOT NULL DEFAULT 0,
    progress             REAL NOT NULL DEFAULT 0,
    draft_ids            JSONB NOT NULL DEFAULT '[]'::jsonb,
    caller_role          VARCHAR(20) NOT NULL,
    caller_commune_id    INTEGER,
    caller_daira_id      INTEGER,
    caller_wilaya_id     INTEGER,
    result               JSONB,
    error                TEXT,
    accept_heartbeat_at  TIMESTAMPTZ,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT chk_generation_job_chunk_counts CHECK (
        total_chunks > 0 AND total_chunks <= 4096
        AND done_chunks >= 0 AND done_chunks <= total_chunks
    ),
    CONSTRAINT chk_generation_job_progress CHECK (progress >= 0 AND progress <= 1),
    CONSTRAINT generation_jobs_commune_fk FOREIGN KEY (commune_id)
        REFERENCES communes (commune_id)
        ON UPDATE NO ACTION ON DELETE RESTRICT,
    CONSTRAINT generation_jobs_created_by_fk FOREIGN KEY (created_by)
        REFERENCES users (id)
        ON UPDATE NO ACTION ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_generation_job_commune
    ON generation_jobs (commune_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_generation_job_status
    ON generation_jobs (status, created_at);

CREATE TABLE IF NOT EXISTS generation_job_chunks (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id               UUID NOT NULL,
    chunk_key            VARCHAR(40) NOT NULL,
    zoom                 INTEGER NOT NULL,
    x0                   INTEGER NOT NULL,
    y0                   INTEGER NOT NULL,
    width                INTEGER NOT NULL,
    height               INTEGER NOT NULL,
    min_lon              DOUBLE PRECISION,
    min_lat              DOUBLE PRECISION,
    max_lon              DOUBLE PRECISION,
    max_lat              DOUBLE PRECISION,
    raster               BYTEA,
    raster_file_name     VARCHAR(255),
    raster_content_type  VARCHAR(30) NOT NULL DEFAULT 'image/jpeg',
    status               VARCHAR(20) NOT NULL DEFAULT 'awaiting_raster'
                             CONSTRAINT chk_generation_job_chunk_status
                             CHECK (status IN ('awaiting_raster', 'ready', 'running', 'done', 'failed', 'cancelled')),
    attempts             INTEGER NOT NULL DEFAULT 0,
    error                TEXT,
    heartbeat_at         TIMESTAMPTZ,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT chk_generation_job_chunk_grid CHECK (
        zoom > 0 AND zoom <= 30
        AND x0 >= 0 AND y0 >= 0
        AND width > 0 AND height > 0
    ),
    CONSTRAINT generation_job_chunks_job_fk FOREIGN KEY (job_id)
        REFERENCES generation_jobs (id)
        ON UPDATE NO ACTION ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_generation_job_chunks_job
    ON generation_job_chunks (job_id);
-- Claim scans: ready chunks and stale running chunks, oldest first.
CREATE INDEX IF NOT EXISTS ix_generation_job_chunks_claim
    ON generation_job_chunks (status, created_at);
CREATE INDEX IF NOT EXISTS ix_generation_job_chunks_heartbeat
    ON generation_job_chunks (heartbeat_at)
    WHERE status = 'running';

COMMENT ON TABLE generation_jobs IS
    'Async "generate roads from urban areas" runs: user-facing job record with progress and result.';
COMMENT ON TABLE generation_job_chunks IS
    'Per-grid-chunk work items of a generation job; rasters uploaded by the client, processed by the nars-api worker pool.';