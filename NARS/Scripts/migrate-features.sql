-- NARS Feature Migration: Leaflet to Maplibre-Geoman
-- This script updates all features to include Maplibre-Geoman compatible properties

\echo ''
\echo '🔄 Starting NARS Feature Migration'
\echo '   Converting Leaflet features to Maplibre-Geoman format...'
\echo ''

-- Start transaction for safety
BEGIN;

-- Create a backup table first
CREATE TABLE IF NOT EXISTS features_backup_ AS SELECT * FROM features;
\echo '✅ Created backup table: features_backup_'

-- Update features with Maplibre-Geoman compatible properties
-- This adds styling properties that Geoman uses for rendering

UPDATE features 
SET data = data || jsonb_build_object(
    -- Geoman-specific identifiers
    '__gm_id', 'feat_' || id::text,
    '__gm_shape', CASE 
        WHEN type = 'houseEntrances' OR type = 'namingPanels' OR type = 'cityCenter' THEN 'marker'
        WHEN type = 'roads' THEN 'line'
        ELSE 'polygon'
    END,
    
    -- Styling properties (matching Leaflet appearance)
    'fillColor', CASE 
        WHEN type = 'areas' AND (data->>'areaTypeKey') = 'central_urban' THEN '#c0392b'
        WHEN type = 'areas' AND (data->>'areaTypeKey') = 'secondary_urban' THEN '#8e44ad'
        WHEN type = 'districts' THEN '#f39c12'
        WHEN type = 'publicBuildings' THEN '#e67e22'
        WHEN type = 'publicSpaces' THEN '#2ecc71'
        WHEN type = 'cityCenter' THEN '#e74c3c'
        ELSE '#8e44ad'
    END,
    
    'fillOpacity', CASE 
        WHEN type = 'areas' THEN 0
        WHEN type = 'districts' THEN 0
        WHEN type = 'publicBuildings' THEN 0.25
        WHEN type = 'publicSpaces' THEN 0.20
        WHEN type = 'cityCenter' THEN 0.5
        ELSE 0.1
    END,
    
    'lineColor', CASE 
        WHEN type = 'areas' AND (data->>'areaTypeKey') = 'central_urban' THEN '#c0392b'
        WHEN type = 'areas' AND (data->>'areaTypeKey') = 'secondary_urban' THEN '#8e44ad'
        WHEN type = 'districts' THEN '#f39c12'
        WHEN type = 'roads' THEN '#3498db'
        WHEN type = 'publicBuildings' THEN '#e67e22'
        WHEN type = 'publicSpaces' THEN '#2ecc71'
        WHEN type = 'cityCenter' THEN '#e74c3c'
        WHEN type = 'houseEntrances' THEN '#27ae60'
        WHEN type = 'namingPanels' THEN '#9b59b6'
        ELSE '#8e44ad'
    END,
    
    'lineWidth', CASE 
        WHEN type = 'roads' THEN 8
        WHEN type = 'publicBuildings' THEN 3
        WHEN type = 'publicSpaces' THEN 3
        WHEN type = 'districts' THEN 3
        WHEN type = 'areas' THEN 2.5
        ELSE 2
    END,
    
    'circleColor', CASE 
        WHEN type = 'cityCenter' THEN '#e74c3c'
        WHEN type = 'houseEntrances' THEN '#27ae60'
        WHEN type = 'namingPanels' THEN '#9b59b6'
        ELSE '#27ae60'
    END,
    
    'circleRadius', CASE 
        WHEN type = 'cityCenter' THEN (COALESCE((data->>'radius')::numeric, 50))::int
        WHEN type = 'houseEntrances' THEN 10
        WHEN type = 'namingPanels' THEN 8
        ELSE 8
    END,
    
    'textColor', CASE 
        WHEN type = 'houseEntrances' THEN '#000000'
        ELSE '#333333'
    END
)
WHERE type IN ('areas', 'districts', 'roads', 'houseEntrances', 'publicBuildings', 'publicSpaces', 'cityCenter', 'namingPanels');

-- Report how many features were updated
\echo ''
SELECT '✅ Updated ' || COUNT(*)::text || ' features with Geoman properties' as status FROM features 
WHERE data ? '__gm_id';

-- Create index for faster Geoman lookups
CREATE INDEX IF NOT EXISTS idx_features_gm_id ON features ((data->>'__gm_id'));
CREATE INDEX IF NOT EXISTS idx_features_type ON features (type);
\echo '✅ Created indexes for Geoman compatibility'

-- Verify migration
\echo ''
\echo '📊 Migration Summary:'
SELECT 
    type,
    COUNT(*) as total_features,
    COUNT(*) FILTER (WHERE data ? '__gm_id') as migrated,
    COUNT(*) FILTER (WHERE NOT (data ? '__gm_id')) as not_migrated
FROM features 
GROUP BY type 
ORDER BY type;

-- Commit transaction
COMMIT;
\echo ''
\echo '✅ Migration completed successfully!'
\echo ''
\echo '💡 Next steps:'
\echo '   1. Test in browser - features should be editable'
\echo '   2. Run cleanup-old-features.sh to remove any duplicates'
\echo ''

-- Rollback instructions (commented out)
-- ROLLBACK;
-- To rollback: UPDATE features f SET data = b.data FROM features_backup_ b WHERE f.id = b.id;
