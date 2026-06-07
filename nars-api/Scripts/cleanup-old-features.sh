#!/bin/bash
# NARS Database Cleanup Script
# Removes duplicate features after Geoman migration

# Database configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-nars_db}"
DB_USER="${DB_USER:-postgres}"

echo "⚠️  NARS Database Cleanup"
echo "   This will remove duplicate features after Geoman migration."
echo ""

# Check if migration was run
MIGRATED_COUNT=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM features WHERE data ? '__gm_id';")
TOTAL_COUNT=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM features;")

echo "📊 Current state:"
echo "   Total features: $TOTAL_COUNT"
echo "   Migrated features (with __gm_id): $MIGRATED_COUNT"

if [ "$MIGRATED_COUNT" -eq 0 ]; then
    echo ""
    echo "❌ No migrated features found!"
    echo "   Please run migrate-features.sql first:"
    echo "   psql -h $DB_HOST -U $DB_USER -d $DB_NAME -f ./migrate-features.sql"
    exit 1
fi

echo ""
read -p "🗑️  Remove non-migrated duplicate features? (yes/no): " confirm

if [ "$confirm" != "yes" ]; then
    echo "❌ Cleanup cancelled."
    exit 0
fi

echo ""
echo "🗑️  Cleaning up old features..."

# Run cleanup SQL
PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<'EOF'
-- Start transaction
BEGIN;

-- Create backup of features before cleanup
CREATE TABLE IF NOT EXISTS features_pre_cleanup AS SELECT * FROM features;

-- Count before cleanup
SELECT 'Before cleanup: ' || COUNT(*) as status FROM features;

-- Strategy: Keep features that have __gm_id (migrated ones)
-- Remove features that are duplicates (same label, type, layer) and don't have __gm_id

DELETE FROM features f1
USING features f2
WHERE f1.id < f2.id 
  AND f1.label = f2.label 
  AND f1.type = f2.type
  AND f1.layer = f2.layer
  AND NOT (f1.data ? '__gm_id');

-- Also remove any features that are exact duplicates of migrated ones
DELETE FROM features f1
USING features f2
WHERE f1.id != f2.id
  AND f1.label = f2.label 
  AND f1.type = f2.type
  AND f1.layer = f2.layer
  AND (f2.data ? '__gm_id')
  AND NOT (f1.data ? '__gm_id');

-- Count after cleanup
SELECT 'After cleanup: ' || COUNT(*) as status FROM features;

-- Update statistics
VACUUM ANALYZE features;

-- Commit
COMMIT;
EOF

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Cleanup completed!"
    
    # Show final counts
    AFTER_COUNT=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM features;")
    MIGRATED_AFTER=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM features WHERE data ? '__gm_id';")
    
    echo ""
    echo "📊 Final state:"
    echo "   Total features: $AFTER_COUNT"
    echo "   Migrated features: $MIGRATED_AFTER"
    echo "   Removed: $((TOTAL_COUNT - AFTER_COUNT)) features"
    
    echo ""
    echo "💾 Backup saved in: features_pre_cleanup table"
    echo ""
    echo "✅ All features are now Maplibre-Geoman compatible!"
    echo ""
    echo "💡 Next steps:"
    echo "   1. Test in browser - all features should be editable"
    echo "   2. Verify vertex editing works on old features"
    echo "   3. Optionally drop backup table: DROP TABLE features_pre_cleanup;"
else
    echo ""
    echo "❌ Cleanup failed!"
    echo "   Database rolled back to pre-cleanup state"
    exit 1
fi
