-- Districts generation phase: widen the two CHECK constraints that gate draft
-- feature types and job stages, and record whether a job asked for the phase.
--
-- 1. ai_draft_features.feature_type gains 'district'. District drafts carry
--    GeoJSON Polygons, exactly like building drafts, so the geometry CHECK
--    gains a matching 'district' -> Polygon branch. Draft districts are
--    reviewable rows, never production features: they are materialized into
--    the districts table only when a reviewer accepts them.
-- 2. generation_jobs.stage gains 'districts'. The phase runs after the roads
--    acceptance pass and partitions the commune's urban zones by the
--    boulevards/avenues (RoadLayers.Primary). 'districts' is 9 characters, so
--    the VARCHAR(10) column still fits without a type change.
-- 3. generation_jobs.generate_districts captures the caller's choice at job
--    creation, so the worker knows to advance the job to the districts stage
--    once acceptance completes instead of finishing the job.
--
-- WHY THE CONSTRAINTS ARE DROPPED BY DEFINITION, NOT BY NAME
-- The table/column header in 0001 warns that this DDL is idempotent BY NAME,
-- and 0002's generation_jobs constraints are indeed named chk_*. But a cluster
-- whose ai_draft_features predates those explicit names has PostgreSQL's
-- auto-generated ones instead (ai_draft_features_feature_type_check,
-- chk_geometry_matches_type) -- 0001's CREATE TABLE IF NOT EXISTS then no-ops
-- and the old names survive forever. A name-only DROP ... IF EXISTS would
-- silently ADD the widened constraints alongside the restrictive originals,
-- leaving 'district' still rejected and the migration looking successful. So
-- every CHECK constraint that mentions the column is dropped (regardless of
-- name) and exactly one correctly-named replacement is added. Verified against
-- a live cluster carrying the legacy auto-generated names.

-- ── ai_draft_features: feature_type + geometry-kind checks ──────────────────
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = 'public.ai_draft_features'::regclass
          AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%feature_type%'
    LOOP
        EXECUTE format('ALTER TABLE public.ai_draft_features DROP CONSTRAINT %I', r.conname);
    END LOOP;
END $$;

ALTER TABLE public.ai_draft_features
    ADD CONSTRAINT chk_ai_draft_feature_type
        CHECK (feature_type IN ('road', 'building', 'district'));

ALTER TABLE public.ai_draft_features
    ADD CONSTRAINT chk_ai_draft_geometry_matches_type CHECK (
        (feature_type = 'road' AND geometry->>'type' = 'LineString')
        OR (feature_type = 'building' AND geometry->>'type' IN ('Polygon', 'MultiPolygon'))
        OR (feature_type = 'district' AND geometry->>'type' = 'Polygon')
    );

-- ── generation_jobs: stage check + the districts-phase flag ─────────────────
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT conname
        FROM pg_constraint
        WHERE conrelid = 'public.generation_jobs'::regclass
          AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%stage%'
    LOOP
        EXECUTE format('ALTER TABLE public.generation_jobs DROP CONSTRAINT %I', r.conname);
    END LOOP;
END $$;

ALTER TABLE public.generation_jobs
    ADD CONSTRAINT chk_generation_job_stage
        CHECK (stage IS NULL OR stage IN ('segment', 'accept', 'districts'));

ALTER TABLE public.generation_jobs
    ADD COLUMN IF NOT EXISTS generate_districts BOOLEAN NOT NULL DEFAULT false;

-- The roads pass already owns generation_jobs.result, so the districts summary
-- gets its own column rather than reshaping a response the web already parses.
ALTER TABLE public.generation_jobs
    ADD COLUMN IF NOT EXISTS districts_result JSONB;

COMMENT ON COLUMN public.generation_jobs.generate_districts IS
    'True when the caller asked for the districts phase to run after roads acceptance (stage = ''districts'').';

COMMENT ON COLUMN public.generation_jobs.districts_result IS
    'DistrictGenerationSummary JSON of the districts phase, set when that phase completes (null when it never ran).';

COMMENT ON COLUMN public.generation_jobs.stage IS
    'Active-phase stage: ''segment'' (chunk segmentation), ''accept'' (roads acceptance), ''districts'' (urban-zone partition into district drafts).';

COMMENT ON TABLE public.ai_draft_features IS
    'AI-suggested road/building/district features awaiting human review before promotion to production feature tables.';