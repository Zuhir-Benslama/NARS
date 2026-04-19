#!/bin/bash
# NARS Database Backup Script
# Creates a timestamped backup of the PostgreSQL database

# Database configuration
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-nars_db}"
DB_USER="${DB_USER:-postgres}"

# Backup directory
BACKUP_DIR="./backups"
mkdir -p "$BACKUP_DIR"

# Timestamp for backup file
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="$BACKUP_DIR/nars_backup_$TIMESTAMP.sql"

echo "🗄️  Starting NARS database backup..."
echo "   Host: $DB_HOST"
echo "   Database: $DB_NAME"
echo "   Backup file: $BACKUP_FILE"

# Create backup using pg_dump
PGPASSWORD="$DB_PASSWORD" pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -F c -f "$BACKUP_FILE"

if [ $? -eq 0 ]; then
    echo "✅ Backup completed successfully!"
    echo "   File: $BACKUP_FILE"
    
    # Compress the backup
    gzip "$BACKUP_FILE"
    echo "   Compressed: ${BACKUP_FILE}.gz"
    
    # List backup files
    echo ""
    echo "📦 Existing backups:"
    ls -lh "$BACKUP_DIR"/*.gz 2>/dev/null | tail -5
else
    echo "❌ Backup failed!"
    exit 1
fi
