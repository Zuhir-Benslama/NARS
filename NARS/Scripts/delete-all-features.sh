#!/bin/bash
# NARS Delete All Features Script
# WARNING: This will PERMANENTLY delete ALL features from phases 0-7

# Database configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-nars_db}"
DB_USER="${DB_USER:-postgres}"

echo "⚠️  ⚠️  ⚠️  WARNING  ⚠️  ⚠️  ⚠️"
echo ""
echo "This will PERMANENTLY DELETE ALL FEATURES from the database:"
echo "  - Phase 0: Areas"
echo "  - Phase 1: Districts"
echo "  - Phase 2: City Center"
echo "  - Phase 3: Roads"
echo "  - Phase 4: House Entrances"
echo "  - Phase 5: Public Buildings"
echo "  - Phase 6: Public Spaces"
echo "  - Phase 7: Naming Panels"
echo ""

# Count features before deletion
echo "📊 Counting features..."
AREAS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM areas;")
DISTRICTS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM districts;")
CITY_CENTERS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM city_centers;")
ROADS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM roads;")
ENTRANCES=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM house_entrances;")
BUILDINGS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM public_buildings;")
SPACES=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM public_spaces;")
PANELS=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM naming_panels;")

TOTAL=$((AREAS + DISTRICTS + CITY_CENTERS + ROADS + ENTRANCES + BUILDINGS + SPACES + PANELS))

echo ""
echo "Current features:"
echo "  - Areas:            $AREAS"
echo "  - Districts:        $DISTRICTS"
echo "  - City Centers:     $CITY_CENTERS"
echo "  - Roads:            $ROADS"
echo "  - House Entrances:  $ENTRANCES"
echo "  - Public Buildings: $BUILDINGS"
echo "  - Public Spaces:    $SPACES"
echo "  - Naming Panels:    $PANELS"
echo "  ─────────────────────────"
echo "  TOTAL:              $TOTAL"
echo ""

read -p "Type 'DELETE ALL' to confirm: " confirm

if [ "$confirm" != "DELETE ALL" ]; then
    echo "❌ Operation cancelled."
    exit 0
fi

echo ""
echo "💾 Creating backup before deletion..."
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="./backups/pre_delete_$TIMESTAMP.sql"
mkdir -p ./backups

PGPASSWORD="$DB_PASSWORD" pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c -f "$BACKUP_FILE"

if [ $? -eq 0 ]; then
    echo "✅ Backup created: ${BACKUP_FILE}.gz"
    gzip "$BACKUP_FILE"
else
    echo "⚠️  Backup failed! Continuing anyway..."
fi

echo ""
echo "🗑️  Deleting all features..."

# Delete all features from all tables
PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" <<EOF
-- Delete all features from all phase tables
DELETE FROM naming_panels;
DELETE FROM public_spaces;
DELETE FROM public_buildings;
DELETE FROM house_entrances;
DELETE FROM roads;
DELETE FROM city_centers;
DELETE FROM districts;
DELETE FROM areas;

-- Update statistics
VACUUM ANALYZE;

-- Verify deletion
SELECT 'Areas: ' || COUNT(*) FROM areas
UNION ALL SELECT 'Districts: ' || COUNT(*) FROM districts
UNION ALL SELECT 'City Centers: ' || COUNT(*) FROM city_centers
UNION ALL SELECT 'Roads: ' || COUNT(*) FROM roads
UNION ALL SELECT 'Entrances: ' || COUNT(*) FROM house_entrances
UNION ALL SELECT 'Buildings: ' || COUNT(*) FROM public_buildings
UNION ALL SELECT 'Spaces: ' || COUNT(*) FROM public_spaces
UNION ALL SELECT 'Panels: ' || COUNT(*) FROM naming_panels;
EOF

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ All features deleted successfully!"
    echo ""
    echo "💡 Next steps:"
    echo "   1. Refresh browser to see empty map"
    echo "   2. Start drawing new features (they'll use Maplibre-Geoman)"
    echo "   3. Or restore from backup if needed:"
    echo "      gunzip -c ${BACKUP_FILE}.gz | psql -h $DB_HOST -U $DB_USER -d $DB_NAME"
else
    echo ""
    echo "❌ Deletion failed!"
    exit 1
fi
