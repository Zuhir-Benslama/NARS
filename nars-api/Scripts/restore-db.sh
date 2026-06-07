#!/bin/bash
# NARS Database Restore Script
# Restores database from a backup file

# Database configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-nars_db}"
DB_USER="${DB_USER:-postgres}"

# Check if backup file is provided
if [ -z "$1" ]; then
    echo "❌ Usage: $0 <backup_file.sql.gz>"
    echo ""
    echo "Available backups:"
    ls -lh ./backups/*.sql.gz 2>/dev/null | tail -10
    exit 1
fi

BACKUP_FILE="$1"

# Check if backup file exists
if [ ! -f "$BACKUP_FILE" ]; then
    echo "❌ Backup file not found: $BACKUP_FILE"
    exit 1
fi

echo "🔄 NARS Database Restore"
echo "   Host: $DB_HOST"
echo "   Database: $DB_NAME"
echo "   Backup file: $BACKUP_FILE"
echo ""
read -p "⚠️  This will OVERWRITE the current database. Continue? (yes/no): " confirm

if [ "$confirm" != "yes" ]; then
    echo "❌ Restore cancelled."
    exit 0
fi

# Create a backup of current database before restore
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
PRE_RESTORE_BACKUP="./backups/pre_restore_$TIMESTAMP.sql"

echo ""
echo "💾 Creating pre-restore backup..."
PGPASSWORD="$DB_PASSWORD" pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c -f "$PRE_RESTORE_BACKUP"

if [ $? -eq 0 ]; then
    echo "✅ Pre-restore backup created: ${PRE_RESTORE_BACKUP}.gz"
    gzip "$PRE_RESTORE_BACKUP"
else
    echo "⚠️  Pre-restore backup failed, continuing anyway..."
fi

# Restore database
echo ""
echo "🔄 Restoring database..."

# Check if file is gzipped
if [[ "$BACKUP_FILE" == *.gz ]]; then
    # Decompress and restore
    gunzip -c "$BACKUP_FILE" | PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME"
else
    # Plain SQL file
    PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$BACKUP_FILE"
fi

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Restore completed successfully!"
    
    # Verify restoration
    echo ""
    echo "📊 Verification:"
    FEATURE_COUNT=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM features;")
    echo "   Features in database: $FEATURE_COUNT"
    
    USER_COUNT=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT COUNT(*) FROM users;")
    echo "   Users in database: $USER_COUNT"
    
    echo ""
    echo "💡 Next steps:"
    echo "   1. Run migrate-features.sql to convert to Maplibre-Geoman format"
    echo "   2. Test in browser"
    echo "   3. Run cleanup-old-features.sh to remove duplicates"
else
    echo ""
    echo "❌ Restore failed!"
    echo "   You can restore from pre-restore backup: ${PRE_RESTORE_BACKUP}.gz"
    exit 1
fi
