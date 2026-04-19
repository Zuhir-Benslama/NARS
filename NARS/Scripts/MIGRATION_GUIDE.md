# NARS Database Migration Guide
## Leaflet-Geoman → Maplibre-Geoman

---

## 📋 Overview

This guide walks you through migrating NARS features from Leaflet-Geoman format to Maplibre-Geoman format.

### What Gets Migrated:
- ✅ All feature types (areas, districts, roads, entrances, buildings, spaces, panels)
- ✅ Feature geometry (coordinates, positions)
- ✅ Feature metadata (labels, decision numbers, dates)
- ✅ Styling properties (colors, opacity, line width)
- ✅ Geoman compatibility identifiers

### Migration Steps:
1. **Restore** from backup (if needed)
2. **Migrate** features to Geoman format
3. **Verify** migration in browser
4. **Cleanup** duplicate features

---

## 🚀 Quick Start

```bash
cd /home/zuhir/Workspace/NARS/Scripts

# Set database password
export DB_PASSWORD="YOUR_DATABASE_PASSWORD"

# Step 1: Restore from backup (optional)
./restore-db.sh ./backups/nars_backup_20260328_*.sql.gz

# Step 2: Run migration
psql -h localhost -U postgres -d nars_db -f migrate-features.sql

# Step 3: Test in browser
# Open http://localhost:5000 and verify features are editable

# Step 4: Cleanup duplicates
./cleanup-old-features.sh
```

---

## 📖 Detailed Instructions

### Step 1: Restore from Backup (Optional)

If you need to restore from a previous backup:

```bash
# List available backups
ls -lh ./backups/

# Restore from specific backup
./restore-db.sh ./backups/nars_backup_20260328_143022.sql.gz
```

**What it does:**
- Creates a pre-restore backup (safety)
- Restores database from compressed backup
- Verifies restoration success

**Output:**
```
🔄 NARS Database Restore
   Host: localhost
   Database: nars_db
   Backup file: ./backups/nars_backup_20260328_143022.sql.gz

💾 Creating pre-restore backup...
✅ Pre-restore backup created

🔄 Restoring database...
✅ Restore completed successfully!

📊 Verification:
   Features in database: 150
   Users in database: 5
```

---

### Step 2: Migrate Features

Run the migration SQL script:

```bash
psql -h localhost -U postgres -d nars_db -f migrate-features.sql
```

**What it does:**
- Creates backup table (`features_backup_`)
- Adds `__gm_id` identifier to each feature
- Adds `__gm_shape` (marker/line/polygon)
- Adds styling properties (fillColor, lineColor, etc.)
- Creates indexes for performance

**Output:**
```
🔄 Starting NARS Feature Migration
   Converting Leaflet features to Maplibre-Geoman format...

✅ Created backup table: features_backup_

✅ Updated 150 features with Geoman properties
✅ Created indexes for Geoman compatibility

📊 Migration Summary:
   type              | total_features | migrated | not_migrated
   ------------------+----------------+----------+--------------
   areas             |             10 |       10 |            0
   districts         |             15 |       15 |            0
   roads             |             50 |       50 |            0
   houseEntrances    |             45 |       45 |            0
   ...

✅ Migration completed successfully!
```

---

### Step 3: Verify Migration

Open the application in your browser and test:

1. **Login** to NARS
2. **Check console** for migration messages:
   ```
   Migrating old features to Geoman...
   ✓ Migrated 150 old features to Geoman
   ```
3. **Right-click on an old feature** → "Edit Geometry"
4. **Verify vertex markers appear** (small circles at corners)
5. **Drag a vertex** → Shape should update
6. **Click Save** → Changes persist after refresh

---

### Step 4: Cleanup Duplicates

After verifying migration works:

```bash
./cleanup-old-features.sh
```

**What it does:**
- Identifies duplicate features
- Keeps migrated features (with `__gm_id`)
- Removes old Leaflet-format duplicates
- Creates `features_pre_cleanup` backup table

**Output:**
```
⚠️  NARS Database Cleanup
   This will remove duplicate features after Geoman migration.

📊 Current state:
   Total features: 300
   Migrated features (with __gm_id): 150

🗑️  Remove non-migrated duplicate features? (yes/no): yes

🗑️  Cleaning up old features...
Before cleanup: 300
After cleanup: 150

✅ Cleanup completed!

📊 Final state:
   Total features: 150
   Migrated features: 150
   Removed: 150 features

✅ All features are now Maplibre-Geoman compatible!
```

---

## 🔧 Troubleshooting

### Migration fails with "connection refused"
```bash
# Check PostgreSQL is running
sudo systemctl status postgresql

# Start if needed
sudo systemctl start postgresql
```

### "permission denied for table features"
```bash
# Grant permissions
psql -h localhost -U postgres -d nars_db -c "GRANT ALL ON features TO postgres;"
```

### Features not editable after migration
```sql
-- Check if migration ran
SELECT COUNT(*) FROM features WHERE data ? '__gm_id';

-- Should return > 0
-- If 0, re-run migration:
psql -h localhost -U postgres -d nars_db -f migrate-features.sql
```

### Need to rollback migration
```sql
-- Rollback to pre-migration state
psql -h localhost -U postgres -d nars_db <<EOF
UPDATE features f 
SET data = b.data 
FROM features_backup_ b 
WHERE f.id = b.id;
EOF
```

### Need to rollback cleanup
```sql
-- Restore from pre-cleanup backup
psql -h localhost -U postgres -d nars_db <<EOF
TRUNCATE features;
INSERT INTO features SELECT * FROM features_pre_cleanup;
DROP TABLE features_pre_cleanup;
EOF
```

---

## 📊 Feature Property Mapping

| Original (Leaflet) | Migrated (Geoman) | Example Value |
|-------------------|-------------------|---------------|
| `type` | `type` (unchanged) | `"areas"` |
| `label` | `label` (unchanged) | `"Kef Bilal"` |
| `coordinates` | `coordinates` (unchanged) | `[{lat, lng}, ...]` |
| *(new)* | `__gm_id` | `"feat_79"` |
| *(new)* | `__gm_shape` | `"polygon"` |
| *(new)* | `fillColor` | `"#8e44ad"` |
| *(new)* | `fillOpacity` | `0` |
| *(new)* | `lineColor` | `"#8e44ad"` |
| *(new)* | `lineWidth` | `2.5` |
| *(new)* | `circleColor` | `"#27ae60"` |
| *(new)* | `circleRadius` | `8` |
| *(new)* | `textColor` | `"#333333"` |

---

## 🗂️ Backup Tables Created

| Table Name | Created By | Purpose |
|------------|-----------|---------|
| `features_backup_` | migrate-features.sql | Pre-migration backup |
| `features_pre_cleanup` | cleanup-old-features.sh | Pre-cleanup backup |
| `nars_backup_*.sql.gz` | backup-db.sh | Timestamped backup file |

**To drop backup tables (after verifying migration):**
```sql
DROP TABLE IF EXISTS features_backup_;
DROP TABLE IF EXISTS features_pre_cleanup;
```

---

## ✅ Verification Checklist

After migration, verify:

- [ ] Console shows "✓ Migrated X old features to Geoman"
- [ ] Right-click on old feature → "Edit Geometry" appears
- [ ] Click "Edit Geometry" → Vertex markers appear
- [ ] Drag vertex → Shape updates in real-time
- [ ] Click "Save" → Changes persist after page refresh
- [ ] Feature colors match original Leaflet appearance
- [ ] Roads have thick blue lines (8px)
- [ ] House entrances have green circle markers
- [ ] Areas/districts have transparent fill with colored borders

---

## 📞 Support

If you encounter issues:

1. Check migration logs in console
2. Verify database connection
3. Check backup tables exist
4. Review troubleshooting section above

**Database logs:**
```bash
# PostgreSQL logs
sudo tail -f /var/log/postgresql/postgresql-*.log
```

**Application logs:**
```bash
# NARS API logs
journalctl -u nars-api -f
```

---

**Last updated:** March 28, 2026
**Version:** 1.0.0
