# NARS Database Backup & Cleanup Guide

## Overview
After migrating features to the new Maplibre-Geoman system, follow these steps to backup and clean up the database.

## ⚠️ Important Notes
- **Always create a backup before cleanup**
- The migration created duplicate features (old Leaflet + new Geoman)
- Cleanup removes duplicates, keeping only Geoman-compatible features

## Step 1: Create Database Backup

```bash
cd /home/zuhir/Workspace/NARS/Scripts

# Set database password
export DB_PASSWORD="YOUR_DATABASE_PASSWORD"

# Run backup script
./backup-db.sh
```

This creates a compressed backup file in `./backups/` with timestamp:
```
nars_backup_20260328_143022.sql.gz
```

## Step 2: Verify Backup

```bash
# List backups
ls -lh ./backups/

# Verify backup file exists and has content
gunzip -l ./backups/nars_backup_*.sql.gz
```

## Step 3: Review What Will Be Cleaned

```bash
# Connect to database and check feature count
psql -h localhost -U postgres -d nars_db -c "SELECT COUNT(*) as total_features FROM features;"

# Check for potential duplicates
psql -h localhost -U postgres -d nars_db -c "
  SELECT type, layer, label, COUNT(*) as count 
  FROM features 
  GROUP BY type, layer, label 
  HAVING COUNT(*) > 1;
"
```

## Step 4: Run Cleanup (Optional)

```bash
# Run cleanup script
./cleanup-old-features.sh
```

This will:
- Remove duplicate features (keeping most recent)
- Keep Geoman-compatible features
- Update database statistics

## Step 5: Verify Cleanup

```bash
# Check feature count after cleanup
psql -h localhost -U postgres -d nars_db -c "SELECT COUNT(*) as total_features FROM features;"
```

## Restore from Backup (If Needed)

```bash
# Restore from a backup file
gunzip -c ./backups/nars_backup_20260328_143022.sql.gz | \
  psql -h localhost -U postgres -d nars_db
```

## Automated Backups (Optional)

Add to crontab for daily backups:
```bash
# Edit crontab
crontab -e

# Add daily backup at 2 AM
0 2 * * * /home/zuhir/Workspace/NARS/Scripts/backup-db.sh
```

## Troubleshooting

### Backup fails with "password authentication failed"
```bash
# Ensure DB_PASSWORD is set
export DB_PASSWORD="YOUR_DATABASE_PASSWORD"
```

### "command not found: pg_dump"
```bash
# Install PostgreSQL client
sudo apt-get install postgresql-client
```

### Cleanup removes wrong features
```bash
# Restore from backup
gunzip -c ./backups/nars_backup_TIMESTAMP.sql.gz | psql -h localhost -U postgres -d nars_db
```

## Contact
For issues, check the NARS logs:
```bash
# View recent API logs
journalctl -u nars-api -n 50
```
