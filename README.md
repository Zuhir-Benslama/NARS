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
| 1 | **Districts** | Polygon | Must share edges per urban area (no gaps), must not overlap, must cover all urban areas. First district in each urban area is exempt from adjacency check. | ✅ Done |
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

### Leaflet / Leaflet-Geoman Loading

Leaflet is loaded from CDN while Leaflet-Geoman is imported as an npm package (`@geoman-io/leaflet-geoman-free`). The CSS is loaded from CDN in `index.html`, and the JavaScript module is imported in `main.ts` before the Vue app bootstraps.

### Vertex Snapping Architecture — *The Snapping Saga* ⚔️

> **Status: RESOLVED.** What follows is the full, honest account of five interlocking bugs that were collectively nicknamed "the Snapping Saga", and precisely how each one was defeated.

Snapping is implemented in `snapping.ts`. Draw mode and edit mode work very differently because Leaflet-Geoman 2.x uses Leaflet's standard event system (`e.latlng` and `map.mouseEventToLayerPoint()`) rather than maintaining its own internal coordinate state.

---

#### Draw mode

`onSnapMove` runs on every `mousemove` event on the map. When a snap point is found within threshold distance, it intercepts the Leaflet event and rewrites `e.latlng` to the snapped coordinate. This forces Leaflet-Geoman to use the snapped coordinate for both the preview line and the placed vertex. A `mousedown` freeze (`snapFrozen`) prevents stray mouse events from clearing snap state before the vertex is committed.

Draw-mode snapping was solid from the start and survived the Saga intact.

---

#### Edit mode — *the epicentre of the Saga*

Edit-mode snapping required solving five separate bugs before it worked correctly end-to-end.

**Bug 1 — Wrong Geoman internal API (`snapping.ts`)**
The original `hookAllEditMarkers` called `pm.getEditor?.()._markers` — a method that does not exist in Leaflet-Geoman 2.19. Vertex markers actually live at `layer.pm._markers`. For polygons this is `L.Marker[][]` (one sub-array per ring); for polylines it is a flat `L.Marker[]`. The fix reads `pm._markers` directly and detects nesting with `Array.isArray(raw[0]) && !(raw[0] instanceof L.Marker)` before flattening one level.

**Bug 2 — "Edit Boundaries" had no commit path (`context-menu.ts`)**
`layer.pm.enable()` shows vertex handles, but `pm:editend` only fires from Geoman's *global* edit mode, not from per-layer `pm.enable()`. The old save listener was therefore a dead end — boundaries could be moved but never persisted. The fix injects a green **"✓ Save Boundaries"** button directly into the map container. Clicking it calls `layer.pm.disable()` (which correctly fires `pm:edit` and `pm:editend` internally), reads the final geometry for polygon or polyline, closes rings where needed, and persists. Pressing ESC cancels without saving.

**Bug 3 — Areas editable when they must not be (`map/index.ts`)**
`updateLayerEditability` was calling `pm.setOptions({ pmIgnore })` at the Leaflet-Geoman handler level only. Geoman's *global* edit mode checks `layer.options.pmIgnore` — the Leaflet layer options object — which was never being set. The fix now writes both: `layer.options.pmIgnore = !editable` (Leaflet level) **and** `pm.setOptions({ pmIgnore: !editable })` (Geoman level).

**Bug 4 — Shadow polygon during editing (`snapping.ts`)**
The original `_snapDragEnd` handler called `layer.setLatLngs(rings)` with **new `L.LatLng` objects**. Geoman's edit handles keep direct references (`_origLatLng`) to the existing objects inside `layer._latlngs`. Replacing those objects broke the references, causing Geoman to render both the pre-drag and post-snap positions simultaneously — the "shadow polygon". The fix mutates the existing `LatLng` objects **in-place** (`ring[idx].lat = snapped.lat; ring[idx].lng = snapped.lng`) and calls `layer.redraw()`. Geoman's internal references stay valid and no ghost appears.

**Bug 5 — Edits not persisted after logout/login (`snapping.ts` + `map/index.ts`)**
Geoman 2.x fires `pm:edit` per vertex-drag **synchronously** from within its own `dragend` handler (registered when `pm.enable()` was called). The old code applied the snap correction inside a `setTimeout(0)`, which runs *after* `pm:edit`'s own save. Result: the backend always received the un-snapped coordinates; the snap correction was purely cosmetic.

Two fixes together seal this:

- **Primary fix (synchronous snap):** `_snapDragEnd` now applies the snap correction *synchronously and immediately*, before returning. Because Geoman's `dragend` handler was registered before ours (it was added at `pm.enable()` time; ours at `hookAllEditMarkers` 100 ms later), Geoman fires first and moves the vertex, then our handler snaps it, then `pm:edit`'s queued `setTimeout(0)` save reads the already-corrected coordinates.

- **Belt-and-suspenders (`pm:editend` in `map/index.ts`):** On edit-session end, all current-phase features are re-saved after a 30 ms delay. This catches any edge case where `pm:edit` fired with intermediate state.

---

#### Snap interceptors

`installSnapInterceptors()` registers permanent `mousemove`, `click`, and `mousedown` handlers on `ctx.map` before Leaflet-Geoman is initialised. These rewrite `e.latlng` on every Leaflet event when a snap is active, ensuring both draw and edit modes always commit the snapped coordinate.

---

#### Snap sources

| Phase | Snaps to |
|---|---|
| Areas | Area polygon rings, municipality boundary |
| Districts | Area rings, other district rings (except dragged), municipality boundary |
| Roads | Road endpoints & midpoints, area rings, district rings |

### Edit Mode — Phase-Restricted Editing

When edit mode is entered, layers belonging to other phases are temporarily removed from `drawnItems` so Leaflet-Geoman cannot select or modify them. Area layers are moved to a separate display-only `L.layerGroup` (remaining visible on the map but uneditable). All other non-current-phase layers are fully hidden. On `pm:editstop`, all layers are restored to `drawnItems` and layer visibility is refreshed.

Phase restriction is enforced at two levels: `layer.options.pmIgnore = true` (the Leaflet layer options object, which Geoman's global edit mode reads) **and** `layer.pm.setOptions({ pmIgnore: true })` (the Geoman PM handler). Both must be set — only setting the PM handler level was the root cause of areas being accidentally editable during the Districts phase (Bug 3 of the Snapping Saga).

### Polygon Geometry Persistence

When a polygon boundary edit is saved, the `pm:edit` handler explicitly closes the ring (repeating the first coordinate as the last) before sending it to the backend, since Leaflet-Geoman's `getLatLngs()` returns an open ring. PostGIS/GEOS requires closed rings and will reject unclosed geometry. After any area edit, `refreshScatteredAreas()` is called to recompute the scattered zone from the new boundary.

Placement validation for polygons uses the vertex centroid (average of all vertex coordinates) rather than the bounding box center. The bounding box center of a concave polygon can fall outside the polygon itself, producing false scattered-area violations.

### Context Menu

Right-clicking any drawn feature opens a context menu with up to four actions:

- **Edit Info** — reopens the feature modal pre-filled with current data (all phases)
- **Edit Boundaries** — activates per-layer Leaflet-Geoman editing on that feature. A green **"✓ Save Boundaries"** button appears at the bottom of the map; clicking it persists the new geometry. Pressing ESC cancels without saving. (Per-layer `pm.enable()` does not fire `pm:editend` on its own — the button is what triggers the save path.)
- **Reverse Direction** — reverses the coordinate order of a road polyline and updates the backend (roads phase only)
- **Remove Object** — deletes the feature with confirmation (current phase only — not shown for features belonging to other phases, preventing accidental deletion of e.g. areas while in the districts phase)

### Layer Visibility

- During the Roads phase, the City Center marker remains visible as a reference point.
- During the House Entrances phase, both the City Center marker and all roads remain visible.
- All other phases show only the current phase's features plus the Areas layer as a permanent reference.

### District Adjacency Validation

The district adjacency rule ("districts must share a boundary — no gaps") is scoped **per urban area**, not globally across the entire commune. Each urban area (main or secondary) is a fully disconnected polygon, so the "must be adjacent" check only applies to sibling districts inside the same area.

The first district drawn inside any urban area is exempt from the adjacency check — exactly as the very first district in the commune is. The backend determines whether a new district is the first in its area by counting existing districts whose geometry intersects the same urban area polygon. This prevents the false rejection that occurred when drawing the first district inside a secondary urban area while districts already existed in the main urban area.

`ValidationController` wraps all stored polygon reads with `ST_MakeValid()` to gracefully handle any legacy or edge-case geometries that may have been saved without a closed ring.

### Feature Data Storage

The `features.data` column is typed as `jsonb` in PostgreSQL, allowing the validation endpoints to run PostGIS queries directly against the stored geometry coordinates without a separate geometry column.
