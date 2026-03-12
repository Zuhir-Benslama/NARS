# NARS — National Addressing Reference System

A full-stack web application for digitizing and managing municipal addressing data. Field operators draw geographic features (urban areas, districts, roads, entrances, public buildings and spaces) on an interactive map, and the system validates, stores, and organizes them into a structured address reference for each commune.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, EF Core 10, Npgsql + PostGIS |
| Frontend | Vue 3, TypeScript, Vite 5 |
| Map | Leaflet 1.9 + Leaflet-Geoman (npm) |
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
| 2 | **City Center** | Circle | 0 or 1 per urban area. Determines entrance numbering direction for roads. | ✅ Done |
| 3 | **Roads** | Polyline | No turn > 90°. Must connect to an existing road (first road exempt). BFS direction algorithm from city center. | ✅ Done |
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

### Leaflet / Leaflet-Geoman Loading

Leaflet is loaded from CDN while Leaflet-Geoman is imported as an npm package (`@geoman-io/leaflet-geoman-free`). The CSS is loaded from CDN in `index.html`, and the JavaScript module is imported in `main.ts` before the Vue app bootstraps.

### Vertex Snapping Architecture — The Snapping Saga (Chapters 1 & 2)

Snapping is implemented in `snapping.ts` and works differently for draw mode and edit mode because Leaflet-Geoman uses Leaflet's standard event system (`e.latlng` and `map.mouseEventToLayerPoint()`), unlike leaflet-draw which maintained its own internal state.

**Draw mode** — `onSnapMove` runs on every `mousemove` event on the map. When a snap point is found within threshold distance, it intercepts the Leaflet event and rewrites `e.latlng` to the snapped coordinate. This forces Leaflet-Geoman to use the snapped coordinate for both the preview line and the placed vertex. A `mousedown` freeze (`snapFrozen`) prevents stray mouse events from clearing snap state before the vertex is committed.

**Edit mode** — `hookEditHandles()` walks through all drawn layers after a 100ms delay (to let Leaflet-Geoman finish activating), then hooks `dragstart`/`dragend` events on every vertex marker found in the layer's PM editor (`_markerGroup`). Named handler references (`marker._snapDragStart`, `marker._snapDragEnd`) replace any previous handlers so ghost midpoint markers (which Leaflet-Geoman converts in-place into real vertices) are always correctly re-hooked. On `dragend`, the snapped coordinate is captured into a local variable *before* `editDragActive` is cleared — clearing the flag first would allow a stray `mousemove` to wipe snap state before it is read.

**Snap interceptors** — `installSnapInterceptors()` registers permanent `mousemove` and `click` handlers on `ctx.map` before Leaflet-Geoman is initialised. These rewrite `e.latlng` on every Leaflet event when snapped, providing a belt-and-suspenders approach that works with both draw and edit modes.

**Snap sources** — districts and areas phases snap to: all area polygon rings, all district polygon rings (except the one being dragged), and the municipality boundary. The areas phase excludes district rings. Roads phase snaps to: road polyline endpoints, road midpoints, area rings, district rings, and city center circle perimeters.

**Geoman's built-in snapping is fully disabled** — `snappable: false` is passed both to `pm.setGlobalOptions()` at init and directly into every `pm.enableDraw(shape, { snappable: false })` call. Without the per-`enableDraw` flag, `setGlobalOptions` alone did not suppress Geoman's draw-mode snap indicator, which jumped erratically to nearby features and completely masked our custom snapping.

**City center circle snapping** — The city center was converted from a `L.Marker` to a `L.Circle` to allow road endpoints to snap onto its perimeter. Snapping to a circle requires different math from snapping to a polyline vertex. The key insight: compute everything in *pixel space* using Leaflet's own internal `circle._point` (rendered center in layer pixels) and `circle._radius` (rendered radius in pixels) — the values Leaflet already computed when it painted the circle. The closest perimeter point is always in the direction of the cursor from the center: `snapPx = center + (cursor - center) / |cursor - center| * radius`. The snap distance is `|cursorDist - radiusPx|`, which is zero when the cursor sits exactly on the visible edge. Earlier approaches that manually converted meters → degrees → pixels drifted significantly at non-equatorial latitudes and varying zoom levels, causing the "repelling magnet" effect where hovering near the circle pushed the snap indicator *away* from it.

### Edit Mode — Phase-Restricted Editing

When edit mode is entered, layers belonging to other phases are temporarily removed from `drawnItems` so Leaflet-Geoman cannot select or modify them. Area layers are moved to a separate display-only `L.layerGroup` (remaining visible on the map but uneditable). All other non-current-phase layers are fully hidden. On `pm:editstop`, all layers are restored to `drawnItems` and layer visibility is refreshed.

### Draw UX — Toolbar-Free, Always-On Drawing

There is no Geoman toolbar. Drawing starts automatically when the phase is entered:

- **On phase entry** — `buildDrawControl()` calls `pm.enableDraw(shape, { snappable: false })` via `setTimeout(0)` so the cursor is immediately in draw mode. City center (circle) is the exception — draw mode activates only after the user confirms the dialog (`cityCenterYes()`), preventing the dialog's OK click from being consumed as an accidental placement.
- **After completing a shape** — `pm:create` re-enables draw mode *after* the modal has closed and the API save has completed, preventing Geoman from re-entering draw mode while the modal is still open (which broke the `pm:create` async handler in earlier iterations).
- **ESC** — cancels the in-progress draw, then re-enables draw mode after 50ms so the user can immediately start a new shape.
- **Finishing a shape** — double-click (polyline) or clicking the first vertex (polygon). The earlier `finishOn: 'click'` option was removed because it caused polylines to finish after exactly two points (click→vertex 1, click vertex 1→finish).
- **Hover popup** — feature info is shown on `mouseover`/`mouseout` using a Leaflet popup with `closeButton: false`, replacing the old click-to-open behavior.

### Road Direction Algorithm

When the user leaves the Roads phase (or manually triggers "Set Road Directions"), `computeAndApplyRoadDirections()` in `road-directions.ts` runs:

1. **BFS from each city center** — finds the closest road endpoint within 200m, orients that road so the city-center-side endpoint is `fromPt`, then propagates direction outward through the connected road network (30m endpoint-proximity threshold). The first city center to reach a shared road wins.
2. **Geographic fallback** — roads not reached by any BFS are oriented by geography: `|Δlat| ≥ |Δlng|` → north-to-south (highest lat first); otherwise east-to-west (highest lng first).
3. **Apply** — clears all existing endpoint arrows, reverses polyline coordinates where needed via `setLatLngs()`, persists the new coordinate order via `PUT /api/update/:id`, then re-adds endpoint arrows (`>` at start, `✕` at end).

### City Center — Circle Instead of Marker

The city center was changed from a `L.Marker` to a `L.Circle` (radius stored in `data.radius`) so that road endpoints can snap to its perimeter. Rules:

- **0 or 1 per urban area** — placing a second circle inside the same area is rejected with an alert. Having zero is valid (the BFS algorithm simply skips areas without a city center and falls back to geographic direction for their roads).
- **Read-only in Roads phase** — right-clicking a city center circle while on the Roads phase shows a locked indicator instead of edit/remove actions.

### Polygon Geometry Persistence

When a polygon boundary edit is saved, the `pm:edit` handler explicitly closes the ring (repeating the first coordinate as the last) before sending it to the backend, since Leaflet-Geoman's `getLatLngs()` returns an open ring. PostGIS/GEOS requires closed rings and will reject unclosed geometry. After any area edit, `refreshScatteredAreas()` is called to recompute the scattered zone from the new boundary.

Placement validation for polygons uses the vertex centroid (average of all vertex coordinates) rather than the bounding box center. The bounding box center of a concave polygon can fall outside the polygon itself, producing false scattered-area violations.

### Context Menu

Right-clicking any drawn feature opens a context menu with up to four actions:

- **Edit Info** — reopens the feature modal pre-filled with current data (all phases)
- **Edit Geometry** — activates Leaflet-Geoman edit mode restricted to that feature (all phases)
- **Set Road Directions** — triggers the BFS direction algorithm for all roads (Roads phase only; also appears on right-clicking the map background)
- **Remove Object** — deletes the feature with confirmation (current phase only — not shown for features belonging to other phases, preventing accidental deletion of e.g. areas while in the districts phase)

City center circles are **read-only** when the current phase is Roads or later — the menu shows a locked indicator instead of the edit/remove actions. ESC dismisses the context menu.

A `_narsFeatureCtxHandled` flag on the map object prevents Leaflet's double-fire of `contextmenu` (layer then map) from overwriting the feature menu with the map-level menu.

### Layer Visibility

- During the Roads phase, the City Center circle remains visible as a reference point. Road endpoint arrows (`>` start, `✕` end) are **not shown** until `computeAndApplyRoadDirections()` has been run — either by leaving the Roads phase or via the right-click "Set Road Directions" menu item.
- During the House Entrances phase, both the City Center circle and all roads (with direction arrows) remain visible.
- All other phases show only the current phase's features plus the Areas layer as a permanent reference.
- On page load, endpoint arrows are only restored for sessions where the Roads phase has already been completed (`currentPhase > roadsPhaseIndex`). Loading mid-Roads phase never shows arrows.

### PostGIS Geometry Repair

`ValidationController` wraps all stored polygon reads with `ST_MakeValid()` to gracefully handle any legacy or edge-case geometries that may have been saved without a closed ring.

### Feature Data Storage

The `features.data` column is typed as `jsonb` in PostgreSQL, allowing the validation endpoints to run PostGIS queries directly against the stored geometry coordinates without a separate geometry column.
