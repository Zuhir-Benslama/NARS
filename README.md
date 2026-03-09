# NARS — National Addressing Reference System

A full-stack web application for digitizing and managing municipal addressing data. Field operators draw geographic features (urban areas, districts, roads, entrances, public buildings and spaces) on an interactive map, and the system validates, stores, and organizes them into a structured address reference for each commune.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, EF Core 10, Npgsql + PostGIS |
| Frontend | Vue 3, TypeScript, Vite 5 |
| Map | Leaflet 1.9 + leaflet-draw 1.0.4 (CDN) |
| Database | PostgreSQL 15+ with PostGIS extension |
| Auth | JWT stored in HttpOnly cookie |

---

## Project Structure

```
Workspace/
├── start.sh                ← First-time setup & run script
│
├── NARS/                   ← ASP.NET Core 10 backend
│   ├── Program.cs          ← App entry point, DI, middleware pipeline
│   ├── NarsApi.csproj      ← NuGet packages
│   ├── appsettings.json    ← DB connection string, JWT config, CORS origins
│   ├── wwwroot/            ← Vite build output (populated by npm run build)
│   ├── Controllers/
│   │   ├── NarsControllerBase.cs    ← Shared AddParam helper
│   │   ├── AuthController.cs        ← /api/signup, /api/signin, /api/logout, /api/current_user
│   │   ├── FeaturesController.cs    ← /api/save, /api/load, /api/update, /api/delete, /api/stats
│   │   ├── LocationsController.cs   ← /api/wilayas, /api/dairas, /api/communes, /api/commune/{id}/boundary
│   │   ├── ValidationController.cs  ← /api/validate/*
│   │   ├── SpatialController.cs     ← /api/road-side, /api/areas/refresh-scattered
│   │   └── PagesController.cs       ← Serves login.html and index.html
│   ├── Data/
│   │   └── AppDbContext.cs          ← EF Core DbContext
│   ├── DTOs/
│   │   └── Dtos.cs                  ← Request / response record types
│   ├── Models/
│   │   ├── Entities.cs              ← User, Feature, Wilaya, Daira, Commune, CommuneBoundary
│   │   └── FeatureTypes.cs          ← Feature type / layer hierarchy constants
│   └── Services/
│       └── JwtService.cs            ← JWT create / validate
│
└── nars-vite/              ← Vue 3 + TypeScript frontend
    ├── index.html          ← Map SPA entry point (served at /map)
    ├── login.html          ← Login / signup page (served at /login)
    ├── vite.config.ts      ← Build config — outDir → ../NARS/wwwroot
    ├── tsconfig.json       ← TypeScript config (strict mode)
    ├── package.json
    └── src/
        ├── main.ts         ← App bootstrap + auth guard
        ├── App.vue         ← Root Vue component
        ├── app.css         ← All application styles
        ├── api.ts          ← Fetch wrapper (credentials: include)
        ├── store.ts        ← Reactive app state + modal bridge
        ├── phases.ts       ← Phase definitions and feature sub-type lists
        ├── validation.ts   ← API calls to /api/validate/*
        ├── types.ts        ← Shared TypeScript interfaces
        ├── leaflet.d.ts    ← Type declarations for global window.L
        ├── map/            ← Leaflet map split into focused modules
        │   ├── index.ts        ← Orchestrator: init, draw control, phase nav, load
        │   ├── state.ts        ← Shared ctx object (map + all layer references)
        │   ├── styles.ts       ← Polygon styles, icons, buildPopup, applyStyle
        │   ├── labels.ts       ← Edge labels, endpoint markers, layer visibility
        │   ├── geometry.ts     ← Spatial helpers, municipality boundary, scattered areas
        │   ├── snapping.ts     ← Full vertex snapping system (draw + edit modes)
        │   ├── context-menu.ts ← Right-click context menu, edit/remove/reverse feature
        │   └── features.ts     ← Feature build/save, fetchRoadSide, computeBisNumber
        └── components/
            ├── PhaseBar.vue           ← Phase navigation stepper
            ├── CityCenterDialog.vue   ← City center placement prompt
            ├── InfoPanel.vue          ← Feature count display
            ├── ProfileMenu.vue        ← User profile dropdown
            └── FeatureModal.vue       ← Feature details form (add + edit modes)
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) and npm
- [PostgreSQL 15+](https://www.postgresql.org/download/) with the [PostGIS extension](https://postgis.net/install/)

---

## Quick Start

A `start.sh` script handles everything for you. Run it from the `Workspace/` directory:

```bash
chmod +x start.sh
./start.sh
```

It will:
1. Check that Node.js and .NET SDK are installed
2. Install npm packages (`npm install`)
3. Restore .NET packages (`dotnet restore`)
4. Ask whether you want **development** or **production** mode
5. Start the app accordingly

---

## Manual Setup

### 1. Configure the database

Edit `NARS/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=nars_db;Username=postgres;Password=YOUR_PASSWORD"
  },
  "Jwt": {
    "SecretKey": "change-this-to-a-long-random-string-in-production",
    "ExpiresInMinutes": 1440
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5000",
      "http://localhost:5173"
    ]
  }
}
```

The database tables are created automatically on first startup via `EnsureCreatedAsync()`.

### 2. Development mode (two terminals)

**Terminal 1 — Backend:**
```bash
cd NARS
dotnet run
# → http://localhost:5000
```

**Terminal 2 — Frontend:**
```bash
cd nars-vite
npm install
npm run dev
# → http://localhost:5173
```

Open **http://localhost:5173** in your browser.
All `/api/*` calls are proxied to `localhost:5000` automatically by Vite.

### 3. Production mode (single server)

```bash
# Build frontend — output goes directly to NARS/wwwroot/
cd nars-vite
npm install
npm run build

# Start backend — serves both API and frontend
cd ../NARS
dotnet run
```

Open **http://localhost:5000** in your browser.

---

## Type Checking

Run the TypeScript compiler without building:

```bash
cd nars-vite
npm run typecheck
```

---

## Mapping Workflow

The app guides the operator through 7 sequential phases. Each phase unlocks the next once its completion criteria are met.

| # | Phase | Draw Type | Validation | Status |
|---|---|---|---|---|
| 0 | **Areas** | Polygon | One main urban area maximum. Scattered area computed automatically. | ✅ Done |
| 1 | **Districts** | Polygon | Must share edges (no gaps), must not overlap, must cover all urban areas. | ✅ Done |
| 2 | **City Center** | Marker | Optional — can be skipped. Determines entrance numbering direction. | ✅ Done |
| 3 | **Roads** | Polyline | No turn > 90°. Must connect to an existing road (first road exempt). | ✅ Done |
| 4 | **House Entrances** | Marker | Assigned to a road. Left side = odd numbers, right side = even numbers. | 🔲 Pending |
| 5 | **Public Buildings** | Polygon | Allowed anywhere including scattered areas. | 🔲 Pending |
| 6 | **Public Spaces** | Polygon | Gardens and squares inside the municipal boundary. | 🔲 Pending |

---

## Feature Types

Features are stored in the database with a `type` and a `layer` (sub-type):

| Type | Layer values |
|---|---|
| `area` | `central_urban`, `secondary_urban`, `scattered` |
| `city_center` | `city_center` |
| `district` | `housing_estate`, `urban_pole`, `district` |
| `road` | `boulevard`, `avenue`, `street`, `drive`, `lane`, `cul_de_sac`, `way` |
| `house_entrance` | `main_entrance`, `secondary_entrance` |
| `public_building` | `public_building` |
| `public_space` | `garden`, `square` |

---

## API Reference

### Auth

| Method | Route | Description |
|---|---|---|
| POST | `/api/signup` | Register a new user |
| POST | `/api/signin` | Log in — sets `access_token` HttpOnly cookie |
| POST | `/api/logout` | Clear the session cookie |
| GET | `/api/current_user` | Get the logged-in user with commune/daira/wilaya info |

### Features

| Method | Route | Description |
|---|---|---|
| POST | `/api/save` | Save a new map feature |
| GET | `/api/load` | Load all features for the current user |
| GET | `/api/load/type/{type}` | Load features filtered by type |
| GET | `/api/load/layer/{layer}` | Load features filtered by layer |
| PUT | `/api/update/{id}` | Update a feature's geometry or label |
| DELETE | `/api/delete/{id}` | Delete a feature |
| GET | `/api/stats` | Feature counts grouped by type |

### Validation

| Method | Route | Description |
|---|---|---|
| POST | `/api/validate/road` | Validate road angle + connectivity |
| POST | `/api/validate/district` | Validate district adjacency + overlap |
| GET | `/api/validate/districts/coverage` | Check districts cover all urban areas |
| GET | `/api/validate/area/main-urban-exists` | Check if a main urban area exists |

### Spatial

| Method | Route | Description |
|---|---|---|
| POST | `/api/road-side` | Determine entrance side (left/right) + suggested number |
| POST | `/api/areas/refresh-scattered` | Recompute the scattered area from the commune boundary |

### Locations

| Method | Route | Description |
|---|---|---|
| GET | `/api/wilayas` | List/search wilayas |
| GET | `/api/dairas?wilaya_id={id}` | List/search dairas in a wilaya |
| GET | `/api/communes?daira_id={id}` | List/search communes in a daira |
| GET | `/api/commune/{id}/boundary` | GeoJSON boundary polygon for a commune |

### Pages

| Method | Route | Description |
|---|---|---|
| GET | `/` | Redirect to `/map` (authenticated) or `/login` |
| GET | `/login` | Serve `login.html` |
| GET | `/map` | Serve `index.html` — redirects to `/login` if not authenticated |

---

## Authentication

The app uses **JWT stored in an HttpOnly cookie** named `access_token`.

- Set by `POST /api/signin` with a 24-hour TTL
- Read automatically by the browser on every subsequent request
- Validated by `JwtService.ValidateToken()` in each protected controller
- Cleared by `POST /api/logout`

The frontend performs a client-side auth guard in `main.ts` — if `/api/current_user` returns 401, the user is immediately redirected to `/login` before Vue mounts.

---

## Database Schema

| Table | Description |
|---|---|
| `users` | Registered users, each linked to a commune |
| `features` | All drawn map features (geometry stored as JSONB) |
| `wilayas` | Algeria's 58 provinces |
| `dairas` | Districts within each wilaya |
| `communes` | Municipalities within each daira |
| `communes_boundaries` | PostGIS polygon geometries for each commune |

Spatial indexes on `communes_boundaries.geometry` and composite indexes on `features(user_id, type, layer)` are created automatically by EF Core.

---

## Notes

### Scattered Areas

Scattered areas are never drawn manually — they are computed automatically by PostGIS as `commune_boundary MINUS ST_Union(all urban areas)` whenever an urban area is saved, edited, or deleted. The frontend calls `POST /api/areas/refresh-scattered` after any of these events and re-renders the result from the returned GeoJSON.

### Leaflet / leaflet-draw Loading

Leaflet and leaflet-draw are loaded from CDN (not bundled by Vite) because `leaflet-draw` is a legacy UMD bundle that requires `window.L` to exist at load time. Both `<script>` tags appear in `index.html` before the Vite module entry point.

### Vertex Snapping Architecture

Snapping is implemented in `snapping.ts` and works differently for draw mode and edit mode because leaflet-draw 1.0.4 bypasses the standard Leaflet event system entirely — it never reads `e.latlng` and never calls `map.mouseEventToLayerPoint()`. Instead it maintains its own internal `_currentLatLng` and `_mouseMarker` state.

**Draw mode** — `onSnapMove` runs on every `document.mousemove` event (capture phase). When a snap point is found within threshold distance, it sets `handler._currentLatLng` and calls `handler._mouseMarker.setLatLng()` directly on the active draw handler, forcing leaflet-draw to use the snapped coordinate for both the preview line and the placed vertex. The active handler is located by walking `drawControl._toolbars.draw._modes` and checking `handler._enabled`. A `mousedown` freeze (`snapFrozen`) prevents the final browser-injected `mousemove` (fired between press and release) from clearing snap state before the vertex is committed.

**Edit mode** — `hookEditHandles()` walks `drawnItems` after a 100ms delay (to let leaflet-draw finish activating), then hooks `dragstart`/`dragend` on every vertex marker found in `editing._markerGroup` and `editing._verticesHandlers[n]._markerGroup`. Named handler references (`marker._snapDragStart`, `marker._snapDragEnd`) replace any previous handlers so ghost midpoint markers (which leaflet-draw converts in-place into real vertices) are always correctly re-hooked. On `dragend`, `snapLatLng` is captured into a local variable *before* `editDragActive` is cleared — clearing the flag first would allow a stray `mousemove` to wipe snap state before it is read. The snapped coordinate is applied via `layer.setLatLngs()` using the closure variable directly (`marker._poly` is unreliable in this version of leaflet-draw).

**Snap interceptors** — `installSnapInterceptors()` registers permanent `mousemove` and `click` handlers on `ctx.map` before leaflet-draw is initialised. These rewrite `e.latlng` on every Leaflet event when snapped, as a belt-and-suspenders complement to the direct `_currentLatLng` assignment.

**Snap sources** — districts and areas phases snap to: all area polygon rings, all district polygon rings (except the one being dragged), and the municipality boundary. The areas phase excludes district rings. Roads phase snaps to: road polyline endpoints, road midpoints, area rings, and district rings.

### Edit Mode — Phase-Restricted Editing

When edit mode is entered, layers belonging to other phases are temporarily removed from `drawnItems` so leaflet-draw cannot select or modify them. Area layers are moved to a separate display-only `L.layerGroup` (remaining visible on the map but uneditable). All other non-current-phase layers are fully hidden. On `EDITSTOP`, all layers are restored to `drawnItems` and layer visibility is refreshed.

### Polygon Geometry Persistence

When a polygon boundary edit is saved, the `EDITED` handler explicitly closes the ring (repeating the first coordinate as the last) before sending it to the backend, since leaflet-draw's `getLatLngs()` returns an open ring. PostGIS/GEOS requires closed rings and will reject unclosed geometry. After any area edit, `refreshScatteredAreas()` is called to recompute the scattered zone from the new boundary.

Placement validation for polygons uses the vertex centroid (average of all vertex coordinates) rather than the bounding box center. The bounding box center of a concave polygon can fall outside the polygon itself, producing false scattered-area violations.

### Context Menu

Right-clicking any drawn feature opens a context menu with up to four actions:

- **Edit Info** — reopens the feature modal pre-filled with current data (all phases)
- **Edit Boundaries** — activates leaflet-draw edit mode restricted to that feature (all phases)
- **Reverse Direction** — reverses the coordinate order of a road polyline and updates the backend (roads phase only)
- **Remove Object** — deletes the feature with confirmation (current phase only — not shown for features belonging to other phases, preventing accidental deletion of e.g. areas while in the districts phase)

### Layer Visibility

- During the Roads phase, the City Center marker remains visible as a reference point.
- During the House Entrances phase, both the City Center marker and all roads remain visible.
- All other phases show only the current phase's features plus the Areas layer as a permanent reference.

### PostGIS Geometry Repair

`ValidationController` wraps all stored polygon reads with `ST_MakeValid()` to gracefully handle any legacy or edge-case geometries that may have been saved without a closed ring.

### Feature Data Storage

The `features.data` column is typed as `jsonb` in PostgreSQL, allowing the validation endpoints to run PostGIS queries directly against the stored geometry coordinates without a separate geometry column.
