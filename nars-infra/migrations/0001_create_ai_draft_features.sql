-- Draft features suggested by the segmentation service (roads + buildings).
-- These never write directly to production feature tables; a field worker
-- or commune admin reviews each row in the nars-web draft queue and either
-- accepts it (copied into the real feature table, with lineage preserved
-- via derived_from_draft_id) or edits/rejects it.
--
-- Geometry is stored as GeoJSON in a JSONB column, matching how the
-- production feature tables carry geometry inside their JSONB `data`
-- column (see FeatureBase.Data in nars-api). The segmentation client
-- already returns GeoJSON, so no geometry type conversion happens at write
-- time. A CHECK constraint validates the GeoJSON "type" field against the
-- row's feature_type (roads -> LineString, buildings -> Polygon/MultiPolygon).

CREATE TABLE IF NOT EXISTS ai_draft_features (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    feature_type    VARCHAR(20) NOT NULL CHECK (feature_type IN ('road', 'building')),
    geometry        JSONB NOT NULL,
    source          VARCHAR(20) NOT NULL DEFAULT 'ai_segmentation',
    confidence      REAL CHECK (confidence IS NULL OR (confidence >= 0 AND confidence <= 1)),
    status          VARCHAR(20) NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending', 'accepted', 'rejected', 'edited')),
    commune_id      INTEGER NOT NULL REFERENCES communes(commune_id),
    reviewed_by     UUID REFERENCES users(id),
    reviewed_at     TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    source_tile_ref VARCHAR(255),

    -- geometry must match feature_type: LineString for roads, Polygon for buildings
    CONSTRAINT chk_geometry_matches_type CHECK (
        (feature_type = 'road' AND geometry->>'type' = 'LineString')
        OR (feature_type = 'building' AND geometry->>'type' IN ('Polygon', 'MultiPolygon'))
    )
);

CREATE INDEX IF NOT EXISTS idx_ai_draft_status
    ON ai_draft_features (feature_type, status, commune_id);

CREATE INDEX IF NOT EXISTS idx_ai_draft_created_at
    ON ai_draft_features (created_at DESC);

COMMENT ON TABLE ai_draft_features IS
    'AI-suggested road/building features awaiting human review before promotion to production feature tables.';
