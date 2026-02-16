-- NARS Database Setup Script
-- PostgreSQL with PostGIS Extension
-- Run this script as postgres superuser

-- Create database
DROP DATABASE IF EXISTS nars_db;
CREATE DATABASE nars_db
    WITH 
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TEMPLATE = template0;

-- Connect to the database
\c nars_db;

-- Enable PostGIS extension
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;

-- Verify PostGIS installation
SELECT PostGIS_Version();

-- Create application user (optional but recommended)
-- DO $$
-- BEGIN
--     IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'nars_user') THEN
--         CREATE ROLE nars_user WITH LOGIN PASSWORD 'your_secure_password_here';
--     END IF;
-- END
-- $$;

-- Grant privileges to application user
-- GRANT ALL PRIVILEGES ON DATABASE nars_db TO nars_user;
-- GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO nars_user;
-- GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO nars_user;

-- Note: Tables will be created automatically by SQLAlchemy when you run server_postgres.py

PRINT 'Database nars_db created successfully with PostGIS extension!';
