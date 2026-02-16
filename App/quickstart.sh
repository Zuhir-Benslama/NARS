#!/bin/bash

# NARS PostgreSQL Quick Start Script
# This script helps you set up PostgreSQL and start the application

echo "=================================================="
echo "NARS - PostgreSQL Setup"
echo "=================================================="
echo ""

# Check if Docker is available
if command -v docker &> /dev/null && command -v docker-compose &> /dev/null; then
    echo "✓ Docker and Docker Compose detected"
    echo ""
    echo "Choose setup method:"
    echo "1) Docker (Recommended - easiest setup)"
    echo "2) Local PostgreSQL installation"
    read -p "Enter choice (1 or 2): " choice
    
    if [ "$choice" = "1" ]; then
        echo ""
        echo "Starting PostgreSQL with Docker..."
        docker-compose up -d postgres
        
        echo ""
        echo "Waiting for PostgreSQL to be ready..."
        sleep 5
        
        echo ""
        echo "✓ PostgreSQL is running!"
        echo "  Host: localhost"
        echo "  Port: 5432"
        echo "  Database: nars_db"
        echo "  Username: postgres"
        echo "  Password: postgres"
        echo ""
        
        # Ask if user wants pgAdmin
        read -p "Start pgAdmin (database management UI)? (y/n): " pgadmin_choice
        if [ "$pgadmin_choice" = "y" ]; then
            docker-compose up -d pgadmin
            echo "✓ pgAdmin started at http://localhost:5050"
            echo "  Login: admin@nars.local / admin"
        fi
    fi
else
    echo "Docker not found. Please install Docker or set up PostgreSQL manually."
    echo "See POSTGRESQL_SETUP.md for instructions."
    echo ""
fi

echo ""
echo "Installing Python dependencies..."
pip install -r requirements.txt --break-system-packages

echo ""
echo "=================================================="
echo "Setup Complete!"
echo "=================================================="
echo ""
echo "To start the application:"
echo "  python server_postgres.py"
echo ""
echo "Then open your browser to:"
echo "  http://localhost:5000"
echo ""
echo "=================================================="
