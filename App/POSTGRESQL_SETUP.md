# PostgreSQL Migration Guide for NARS

## 🎯 Overview

This guide will help you migrate NARS from SQLite to PostgreSQL with PostGIS extension for better scalability and geographic data support.

## 📋 Prerequisites

### 1. Install PostgreSQL

**Ubuntu/Debian:**
```bash
sudo apt update
sudo apt install postgresql postgresql-contrib postgis
```

**macOS (using Homebrew):**
```bash
brew install postgresql postgis
brew services start postgresql
```

**Windows:**
1. Download from: https://www.postgresql.org/download/windows/
2. Run installer and install PostgreSQL 15+
3. Make sure to install PostGIS during setup

**Docker (Recommended for Development):**
```bash
docker run --name nars-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=nars_db \
  -p 5432:5432 \
  -d postgis/postgis:15-3.3
```

### 2. Verify PostgreSQL Installation

```bash
# Check PostgreSQL version
psql --version

# Should show: psql (PostgreSQL) 15.x or higher
```

## 🚀 Setup Steps

### Step 1: Create Database

**Option A: Using psql (Command Line)**

```bash
# Login as postgres user
sudo -u postgres psql

# Run the setup script
\i setup_database.sql

# Or create manually:
CREATE DATABASE nars_db;
\c nars_db
CREATE EXTENSION postgis;
\q
```

**Option B: Using pgAdmin (GUI)**

1. Open pgAdmin
2. Right-click on "Databases" → Create → Database
3. Name: `nars_db`
4. Click "Save"
5. Right-click on `nars_db` → Query Tool
6. Run: `CREATE EXTENSION postgis;`

**Option C: Using Docker**

```bash
# The Docker image already has PostGIS installed
# Just create the database:
docker exec -it nars-postgres psql -U postgres -c "CREATE DATABASE nars_db;"
docker exec -it nars-postgres psql -U postgres -d nars_db -c "CREATE EXTENSION postgis;"
```

### Step 2: Configure Database Connection

Edit `server_postgres.py` (around line 17):

```python
DATABASE_CONFIG = {
    'host': 'localhost',      # Change if using remote database
    'port': 5432,             # Default PostgreSQL port
    'database': 'nars_db',    # Database name
    'user': 'postgres',       # Your PostgreSQL username
    'password': 'postgres'    # CHANGE THIS to your password!
}
```

**Security Note:** For production, use environment variables:

```python
import os

DATABASE_CONFIG = {
    'host': os.getenv('DB_HOST', 'localhost'),
    'port': int(os.getenv('DB_PORT', 5432)),
    'database': os.getenv('DB_NAME', 'nars_db'),
    'user': os.getenv('DB_USER', 'postgres'),
    'password': os.getenv('DB_PASSWORD', 'postgres')
}
```

Then create a `.env` file:
```
DB_HOST=localhost
DB_PORT=5432
DB_NAME=nars_db
DB_USER=postgres
DB_PASSWORD=your_secure_password
```

### Step 3: Install Python Dependencies

```bash
pip install -r requirements.txt --break-system-packages
```

New dependencies:
- `psycopg2-binary` - PostgreSQL adapter
- `SQLAlchemy` - ORM for database operations

### Step 4: Run the Server

```bash
python server_postgres.py
```

You should see:
```
==================================================
NARS - National Addressing Reference System
PostgreSQL Backend Server
==================================================
✓ Database tables created successfully!
Loading locations from CSV...
✓ Loaded 1543 locations into database
✓ Database initialization complete!

==================================================
Server starting on http://localhost:5000
==================================================
```

### Step 5: Verify Database

```bash
# Connect to database
psql -U postgres -d nars_db

# Check tables
\dt

# Expected output:
#  Schema |   Name    | Type  |  Owner
# --------+-----------+-------+----------
#  public | features  | table | postgres
#  public | locations | table | postgres
#  public | users     | table | postgres

# Check PostGIS
SELECT PostGIS_Version();

# Check location count
SELECT COUNT(*) FROM locations;
# Should return: 1543

\q
```

## 📊 Database Schema

### Tables Created

**features** - Stores map features
```sql
CREATE TABLE features (
    id SERIAL PRIMARY KEY,
    type VARCHAR(50) NOT NULL,
    layer VARCHAR(50) NOT NULL,
    label VARCHAR(255) NOT NULL,
    data TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

**users** - Stores user accounts
```sql
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    phone VARCHAR(50) NOT NULL,
    username VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    wilaya VARCHAR(100) NOT NULL,
    daira VARCHAR(100) NOT NULL,
    commune VARCHAR(100) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

**locations** - Stores geographic locations
```sql
CREATE TABLE locations (
    id SERIAL PRIMARY KEY,
    wilaya VARCHAR(100) NOT NULL,
    daira VARCHAR(100) NOT NULL,
    commune VARCHAR(100) NOT NULL
);
```

## 🔄 Migrating Existing Data (If you have SQLite data)

If you already have users or features in SQLite, migrate them:

```python
# migration_script.py
import sqlite3
import psycopg2

# Connect to SQLite
sqlite_conn = sqlite3.connect('vault.sqlite')
sqlite_cursor = sqlite_conn.cursor()

# Connect to PostgreSQL
pg_conn = psycopg2.connect(
    host='localhost',
    database='nars_db',
    user='postgres',
    password='postgres'
)
pg_cursor = pg_conn.cursor()

# Migrate users
print("Migrating users...")
sqlite_cursor.execute("SELECT name, email, phone, username, password_hash, wilaya, daira, commune FROM users")
users = sqlite_cursor.fetchall()

for user in users:
    try:
        pg_cursor.execute("""
            INSERT INTO users (name, email, phone, username, password_hash, wilaya, daira, commune)
            VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
        """, user)
    except Exception as e:
        print(f"Error migrating user {user[3]}: {e}")

pg_conn.commit()
print(f"Migrated {len(users)} users")

# Migrate features
print("Migrating features...")
sqlite_conn2 = sqlite3.connect('map_features.db')
sqlite_cursor2 = sqlite_conn2.cursor()

sqlite_cursor2.execute("SELECT type, layer, label, data FROM features")
features = sqlite_cursor2.fetchall()

for feature in features:
    pg_cursor.execute("""
        INSERT INTO features (type, layer, label, data)
        VALUES (%s, %s, %s, %s)
    """, feature)

pg_conn.commit()
print(f"Migrated {len(features)} features")

# Close connections
sqlite_conn.close()
sqlite_conn2.close()
pg_conn.close()

print("Migration complete!")
```

Run it:
```bash
python migration_script.py
```

## 🔧 Performance Optimization

### Create Indexes

```sql
-- Connect to database
psql -U postgres -d nars_db

-- Create indexes for better performance
CREATE INDEX idx_features_type ON features(type);
CREATE INDEX idx_features_layer ON features(layer);
CREATE INDEX idx_users_username ON users(username);
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_locations_wilaya ON locations(wilaya);
CREATE INDEX idx_locations_daira ON locations(daira);
CREATE INDEX idx_locations_commune ON locations(commune);
```

### Connection Pooling

Already configured in `server_postgres.py`:
- Pool size: 10 connections
- Max overflow: 20 additional connections
- Pool timeout: 30 seconds
- Pool recycle: 1 hour

## 🐛 Troubleshooting

### Error: "psycopg2.OperationalError: FATAL: password authentication failed"

**Solution:**
1. Check your password in `server_postgres.py`
2. Reset PostgreSQL password:
```bash
sudo -u postgres psql
ALTER USER postgres PASSWORD 'new_password';
\q
```

### Error: "could not connect to server: Connection refused"

**Solution:**
1. Check if PostgreSQL is running:
```bash
sudo systemctl status postgresql  # Linux
brew services list                 # macOS
```

2. Start PostgreSQL:
```bash
sudo systemctl start postgresql    # Linux
brew services start postgresql     # macOS
```

### Error: "database nars_db does not exist"

**Solution:**
```bash
sudo -u postgres psql
CREATE DATABASE nars_db;
\q
```

### Error: "extension postgis does not exist"

**Solution:**
```bash
# Install PostGIS
sudo apt install postgis  # Ubuntu/Debian

# Enable in database
psql -U postgres -d nars_db
CREATE EXTENSION postgis;
\q
```

### Error: "too many connections"

**Solution:**
1. Check current connections:
```sql
SELECT count(*) FROM pg_stat_activity;
```

2. Increase max_connections in postgresql.conf:
```bash
sudo nano /etc/postgresql/15/main/postgresql.conf
# Change: max_connections = 100
sudo systemctl restart postgresql
```

## 📈 Monitoring

### Check Active Connections

```sql
SELECT 
    datname,
    count(*) as connections
FROM pg_stat_activity
GROUP BY datname;
```

### Check Table Sizes

```sql
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

### Check Database Size

```sql
SELECT pg_size_pretty(pg_database_size('nars_db'));
```

## 🔐 Security Best Practices

1. **Change default passwords**
2. **Use environment variables** for credentials
3. **Enable SSL connections** for production
4. **Create separate user** for application:

```sql
CREATE USER nars_user WITH PASSWORD 'secure_password';
GRANT ALL PRIVILEGES ON DATABASE nars_db TO nars_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO nars_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO nars_user;
```

Update `DATABASE_CONFIG` to use `nars_user` instead of `postgres`.

## ✅ Verification Checklist

- [ ] PostgreSQL installed and running
- [ ] PostGIS extension enabled
- [ ] Database `nars_db` created
- [ ] Python dependencies installed
- [ ] Database credentials configured
- [ ] Server starts without errors
- [ ] Can create new user account
- [ ] Can login successfully
- [ ] Map loads with user's commune
- [ ] Can create map features
- [ ] Features persist after restart

## 🎉 Success!

You've successfully migrated to PostgreSQL with PostGIS! Your application now has:

✅ **Better Scalability** - Handle thousands of concurrent users
✅ **Connection Pooling** - Efficient database connections
✅ **PostGIS Support** - Native geographic data handling
✅ **ACID Compliance** - Data integrity guaranteed
✅ **Production Ready** - Enterprise-grade database

## 📞 Need Help?

Check the main README.md for additional support and documentation.
