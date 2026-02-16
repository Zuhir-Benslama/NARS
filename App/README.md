# NARS - National Addressing Reference System

A scalable web application for managing geographic data and administrative boundaries with user authentication and PostgreSQL/PostGIS backend.

## 🚀 New: PostgreSQL Migration

NARS now uses **PostgreSQL with PostGIS** for better scalability and geographic data support!

### Quick Start with Docker (Recommended)

```bash
# 1. Start PostgreSQL
docker-compose up -d postgres

# 2. Install dependencies
pip install -r requirements.txt --break-system-packages

# 3. Run the server
python server_postgres.py

# 4. Open browser
http://localhost:5000
```

📖 **Detailed Setup:** See [POSTGRESQL_SETUP.md](POSTGRESQL_SETUP.md)

## Features

🔐 **User Authentication**
- Sign up with name, email, phone number, username, password, wilaya, daira, and commune
- Each user is assigned to a specific commune during registration
- Real-time searchable dropdowns with instant filtering
- Type to see filtered suggestions as you type (300ms debounce)
- Three-tier location hierarchy with proper filtering:
  - **Wilaya:** Search from 48 Algerian states
  - **Daira:** Automatically filtered to show only dairas in selected wilaya
  - **Commune:** Automatically filtered to show only communes in selected daira
- Location data stored in SQLite for fast queries
- Email and username must be unique
- Secure password hashing
- Session-based authentication
- User data stored in separate vault.sqlite database
- Sign in/out functionality
- **Upon login, users are automatically taken to their assigned commune**

✨ **Built-in Leaflet Draw Tools**
- 📍 **Markers**: Click the marker tool and place on the map
- 📏 **Polylines**: Click points to draw lines, double-click to finish
- ⬟ **Polygons**: Click points to draw polygons, double-click to close
- ✏️ **Edit & Delete**: Modify or remove existing features

🏷️ **Permanent Label System**
- After drawing each element, a popup appears to add a label
- Labels are displayed permanently on the map (always visible)
- Click any feature to see additional details in a popup

📍 **Municipal Boundaries**
- User's commune boundary is automatically displayed upon login
- Map automatically centers on the user's assigned commune
- Red outline with no fill
- Hover over boundaries to see municipality names
- Click for more details

💾 **Database Storage**
- All features are automatically saved to SQLite database
- Load saved features from previous sessions
- Features organized by type (zones, districts, equipments, numbers, panels, polylines)

🎨 **User Interface**
- **Profile Menu** (Top Right):
  - Displays username and full name
  - Settings option (coming soon)
  - Log out functionality
- **Layer Control** (Bottom Left):
  - Switch between different map views (Satellite, Street, Topographic, Light, Dark)
- **Info Panel** (Bottom Right):
  - Real-time feature counters

## Technology Stack

- **Frontend**: Leaflet.js with Leaflet.Draw plugin (built-in drawing controls)
- **Backend**: Python Flask with SQLAlchemy ORM
- **Database**: PostgreSQL 15+ with PostGIS extension
- **Connection Pooling**: SQLAlchemy connection pool (10 base + 20 overflow)
- **Authentication**: Werkzeug password hashing + Flask sessions
- **Geographic Data**: PostGIS for spatial operations
- **UI**: Custom HTML/CSS/JavaScript

## Installation

⚠️ **Database:** NARS now uses PostgreSQL with PostGIS for better scalability!

### Prerequisites
- Python 3.7 or higher
- PostgreSQL 15+ with PostGIS extension
- Docker (optional but recommended)

### Quick Setup (Docker - Recommended)

1. **Start PostgreSQL with Docker**
   ```bash
   docker-compose up -d postgres
   ```

2. **Install Python Dependencies**
   ```bash
   pip install -r requirements.txt --break-system-packages
   ```

3. **Run the Server**
   ```bash
   python server_postgres.py
   ```

4. **Open in Browser**
   - Navigate to: `http://localhost:5000`

### Manual Setup (Without Docker)

1. **Install PostgreSQL**
   
   **Ubuntu/Debian:**
   ```bash
   sudo apt install postgresql postgresql-contrib postgis
   ```
   
   **macOS:**
   ```bash
   brew install postgresql postgis
   brew services start postgresql
   ```
   
   **Windows:**
   - Download from: https://www.postgresql.org/download/windows/

2. **Create Database**
   ```bash
   sudo -u postgres psql
   CREATE DATABASE nars_db;
   \c nars_db
   CREATE EXTENSION postgis;
   \q
   ```

3. **Configure Connection**
   
   Edit `server_postgres.py` line 17-23 with your database credentials:
   ```python
   DATABASE_CONFIG = {
       'host': 'localhost',
       'port': 5432,
       'database': 'nars_db',
       'user': 'postgres',
       'password': 'your_password'  # Change this!
   }
   ```

4. **Install Dependencies and Run**
   ```bash
   pip install -r requirements.txt --break-system-packages
   python server_postgres.py
   ```

📖 **Detailed Instructions:** See [POSTGRESQL_SETUP.md](POSTGRESQL_SETUP.md)

## Usage Guide

### Authentication

**First Time Users:**
1. Navigate to `http://localhost:5000`
2. Click the "Sign Up" tab
3. Fill in your details:
   - Full Name
   - **Email** (must be unique)
   - Phone Number
   - Username (must be unique)
   - Password
   - Search and select your Wilaya (State) - searchable dropdown
   - Search and select your Daira - searchable dropdown
   - Search and select your Commune (Municipality) - searchable dropdown
4. Click "Sign Up"
5. You'll be automatically redirected to sign in

**Returning Users:**
1. Navigate to `http://localhost:5000`
2. Enter your username and password
3. Click "Sign In"
4. You'll be redirected to the map interface

**Logging Out:**
1. Click on your profile in the top-right corner
2. Click "Log Out"
3. You'll be redirected to the login page

### Drawing Features

1. **Add a Marker**
   - Click the marker icon (📍) in the drawing toolbar on the left
   - Click anywhere on the map to place the marker
   - A popup will appear - enter a label
   - Click "Save" to add the marker with its label permanently visible

2. **Draw a Polyline**
   - Click the polyline icon (📏) in the drawing toolbar
   - Click multiple points on the map to create your line
   - Double-click to finish the line
   - A popup will appear - enter a label
   - Click "Save" to add the polyline with its label permanently visible

3. **Draw a Polygon**
   - Click the polygon icon (⬟) in the drawing toolbar
   - Click multiple points on the map to create your polygon
   - Double-click to close the polygon
   - A popup will appear - enter a label
   - Click "Save" to add the polygon with its label permanently visible

4. **Edit Features**
   - Click the edit icon (✏️) in the drawing toolbar
   - Click and drag vertices to modify shapes
   - Click "Save" when done editing

5. **Delete Features**
   - Click the delete icon (🗑️) in the drawing toolbar
   - Click on features you want to remove
   - Click "Save" to confirm deletion

### Managing Data

- **Load Data**: Click "📂 Load Data" button in the top-right to retrieve saved features from the database
- **Clear All**: Click "🗑️ Clear All" button to remove all features (confirmation required)
- **View Stats**: Check the info panel in the bottom-right corner for feature counts

### Viewing Features

- Labels are permanently visible on all features
- Click on any marker, polyline, or polygon to see additional details in a popup
- Upon login, the map automatically centers on your assigned commune (municipality)
- Your commune's administrative boundary is displayed automatically
- The map fits to show your entire commune area

**UI Layout:**
- **Top Right:** Profile menu with username, name, and logout option
- **Bottom Left:** Layer control for switching map views
- **Bottom Right:** Feature counters showing real-time statistics

## File Structure

```
.
├── login.html                 # Authentication page
├── map_app.html               # Main HTML interface
├── app.js                     # Frontend JavaScript logic
├── server_postgres.py         # Flask backend with PostgreSQL (USE THIS)
├── server.py                  # OLD: SQLite version (deprecated)
├── docker-compose.yml         # Docker setup for PostgreSQL
├── setup_database.sql         # PostgreSQL initialization script
├── quickstart.sh              # Quick setup script
├── algeria_cities.csv         # Wilayas, dairas, and communes data
├── Boundaries.geojson         # Municipal boundaries data
├── requirements.txt           # Python dependencies
├── POSTGRESQL_SETUP.md        # Detailed PostgreSQL setup guide
├── SETUP.md                   # OLD: SQLite setup (deprecated)
└── README.md                  # This file
```

## Database Schema

The application uses **PostgreSQL 15+ with PostGIS extension**.

### Database: nars_db

**users** - User Authentication
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

**features** - Map Features
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

**locations** - Geographic Data
```sql
CREATE TABLE locations (
    id SERIAL PRIMARY KEY,
    wilaya VARCHAR(100) NOT NULL,
    daira VARCHAR(100) NOT NULL,
    commune VARCHAR(100) NOT NULL
);
```

### Connection Pooling

Configured for high performance:
- **Pool Size:** 10 permanent connections
- **Max Overflow:** 20 additional connections
- **Pool Timeout:** 30 seconds
- **Pool Recycle:** 1 hour (prevents stale connections)

## API Endpoints

**Authentication:**
- `GET /` - Redirect to login or map
- `GET /login` - Login page
- `GET /map` - Map interface (requires authentication)
- `POST /api/signup` - Register new user
- `POST /api/signin` - Authenticate user
- `POST /api/logout` - End session
- `GET /api/current_user` - Get current logged-in user's information (requires authentication)

**Locations:**
- `GET /api/wilayas?search=<query>` - Get filtered wilayas
- `GET /api/dairas?wilaya=<wilaya>&search=<query>` - Get filtered dairas for wilaya
- `GET /api/communes?wilaya=<wilaya>&daira=<daira>&search=<query>` - Get filtered communes

**Features:**
- `POST /api/save` - Save a feature to database
- `GET /api/load` - Load all features from database
- `POST /api/clear` - Clear all features
- `DELETE /api/delete/<id>` - Delete a specific feature
- `GET /api/stats` - Get feature statistics
- `GET /api/load/layer/<type>` - Load features by layer
- `GET /api/load/type/<type>` - Load features by type

## Keyboard Shortcuts

- **Enter**: Save label in popup
- **Escape**: Cancel label entry or exit edit/delete mode
- **Double-click**: Finish polyline/polygon drawing

## Customization

### Change Map Center
Edit line 2 in `app.js`:
```javascript
const map = L.map('map').setView([36.7538, 3.0588], 10); // Algiers, Algeria
```

### Change Colors
Edit the color values in `app.js`:
- Polylines: `color: '#3498db'` (blue)
- Zones: `color: '#9b59b6'` (purple)
- Districts: `color: '#f39c12'` (orange)
- Equipments: `color: '#16a085'` (teal)
- Boundaries: `color: '#e74c3c'` (red)

### Change Map Tiles
Replace the tile layer in `app.js` with any other tile provider:
```javascript
L.tileLayer('YOUR_TILE_URL', {
    attribution: 'YOUR_ATTRIBUTION'
}).addTo(map);
```

## Troubleshooting

**Server won't start**
- Make sure Flask is installed: `pip install flask --break-system-packages`
- Check if port 5000 is already in use

**Features not saving**
- Check the browser console for errors
- Verify the database file has write permissions

**Map not displaying**
- Check your internet connection (tiles are loaded from OpenStreetMap)
- Check the browser console for errors

## License

This project uses:
- Leaflet.js (BSD 2-Clause License)
- OpenStreetMap tiles (ODbL License)

## Future Enhancements

Potential features to add:
- Edit existing features
- Delete individual features
- Export data as GeoJSON
- Import GeoJSON data
- Multiple map layers
- Feature search/filter
- Color customization per feature
- Measurement tools

---

**Enjoy mapping!** 🗺️
