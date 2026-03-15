# NARS — National Addressing Reference System

A full-stack GIS web application for Algerian municipal addressing.  
**Stack:** ASP.NET Core 10 · Vue 3 · TypeScript · Vite 8 · Leaflet · Leaflet-Geoman 2.19.2 · Turf.js · Graphology · vue-i18n 10 · @vueuse/core

---

## Project Status

**Version 1.1-Alpha** — All 8 mapping phases implemented end-to-end. Significant refactoring, theming, i18n, and database restructuring completed since Pre-Alpha.

---

## Mapping Phases

| # | Key | Label | Draw Type | Status | Description |
|---|-----|-------|-----------|--------|-------------|
| 0 | `areas` | Areas | Polygon | ✅ Done | Urban areas (Main Urban, Secondary Urban). Scattered areas computed automatically. |
| 1 | `districts` | Districts | Polygon | ✅ Done | Districts inside urban areas. Must share edges — no gaps allowed. |
| 2 | `cityCenter` | City Center | Circle | ✅ Done | City center circle per urban area. Determines road direction and house entrance numbering. |
| 3 | `roads` | Roads | Polyline | ✅ Done | Roads inside the municipal limit. Must connect to at least one other road. |
| 4 | `houseEntrances` | House Entrances | Marker | ✅ Done | Main entrances (left = odd, right = even) and secondary entrances (BIS01, BIS02…). |
| 5 | `publicBuildings` | Public Buildings | Polygon | ✅ Done | Public buildings. Allowed everywhere including scattered areas. |
| 6 | `publicSpaces` | Public Spaces | Polygon | ✅ Done | Public spaces (gardens, squares) inside the municipal limit. |
| 7 | `namingPanels` | Naming Panels | Marker | ✅ Done | Auto-generated signage panels derived from existing features. |

### Phase Visibility Matrix

Each phase shows only the layers relevant to the operator's current task.

| Active Phase | Visible layers |
|---|---|
| Areas | Areas |
| Districts | Areas · Districts |
| City Center | Areas · City Center |
| Roads | Areas · City Center · Roads |
| House Entrances | Areas · City Center · Roads · House Entrances |
| Public Buildings | Areas · Public Buildings |
| Public Spaces | Areas · Public Spaces |
| Naming Panels | Areas · Districts · Roads · Public Buildings · Public Spaces · Naming Panels |

---

## Architecture

```
src/
├── map/
│   ├── index.ts             # Map orchestrator — initMap, phase navigation, re-exports
│   ├── draw-control.ts      # buildDrawControl, updateLayerEditability
│   ├── draw-events.ts       # All pm:* and Leaflet map event wiring
│   ├── create-handler.ts    # pm:create business logic, bindHoverPopup, getDistrictLabel
│   ├── loader.ts            # loadFromDatabase, loadUserAndCommune
│   ├── context-menu.ts      # Menu DOM, bindContextMenu, showMapContextMenu
│   ├── edit-actions.ts      # removeFeature, editGeometry, editFeatureInfo, window globals
│   ├── house-entrances.ts   # setReferenceRoad/Entrance, setHouseNumbers algorithm
│   ├── state.ts             # Shared ctx object (map, drawnItems, layers)
│   ├── snapping.ts          # Custom vertex snapping (endpoint + city center perimeter)
│   ├── road-directions.ts   # Road direction algorithm
│   ├── features.ts          # buildFeatureData, saveToDatabase, prepareModalExtras
│   ├── labels.ts            # Labels, edge labels, endpoint arrows, PHASE_VISIBILITY table
│   ├── geometry.ts          # Spatial helpers
│   ├── naming-panels.ts     # Auto-generate naming panels (display-only, per-source colors)
│   └── styles.ts            # Phase-specific Leaflet style objects
├── components/
│   ├── PhaseBar.vue         # Phase navigation bar
│   ├── FeatureModal.vue     # Feature creation / edit dialog
│   ├── InfoPanel.vue        # Sidebar info panel
│   ├── ProfileMenu.vue      # User profile menu
│   └── SettingsModal.vue    # Settings: language, theme, account, feature types
├── composables/
│   └── useTheme.ts          # Singleton theme composable (light / dark / auto)
├── locales/
│   ├── en.json              # English translations
│   ├── fr.json              # French translations
│   └── ar.json              # Arabic translations
├── store.ts                 # Reactive Vue store (AppStore)
├── phases.ts                # Phase definitions, area/district/road/space types
├── types.ts                 # TypeScript interfaces
├── i18n.ts                  # vue-i18n instance, setLang, applyInitialLang
├── api.ts                   # apiFetch wrapper
└── validation.ts            # Client-side validation helpers
```

---

## What Changed in v1.1-Alpha

### Frontend — Code Organisation

`src/map/index.ts` was 930 lines. It has been split into 5 focused modules:

| New file | Responsibility | Lines |
|---|---|---|
| `index.ts` | `initMap`, phase navigation | 157 |
| `draw-control.ts` | `buildDrawControl`, `updateLayerEditability` | 71 |
| `draw-events.ts` | All `pm:*` + map event wiring | 273 |
| `create-handler.ts` | `pm:create` per-phase business logic | 297 |
| `loader.ts` | `loadFromDatabase`, `loadUserAndCommune` | 169 |

`src/map/context-menu.ts` was 590 lines. Split into 3 modules:

| New file | Responsibility | Lines |
|---|---|---|
| `context-menu.ts` | Menu DOM, `bindContextMenu`, `showMapContextMenu` | 152 |
| `edit-actions.ts` | `removeFeature`, `editGeometry`, `editFeatureInfo`, `window.__nars*` | 250 |
| `house-entrances.ts` | Reference road/entrance state, `setHouseNumbers` | 134 |

### Frontend — Internationalisation

Migrated from a custom hand-rolled i18n implementation to **vue-i18n v10** (Composition API mode, `legacy: false`).

- `en` locale is bundled inline; `fr` and `ar` are lazy-loaded on first use via dynamic `import()`.
- All Vue components use `const { t } = useI18n()` — Vue's dependency tracking applies correctly in templates.
- Non-component TypeScript files (`map/*.ts`) import `t()` directly from `src/i18n.ts` — same signature as before, zero call-site changes.
- `setLang(lang)` handles RTL direction, localStorage persistence, Geoman sync, and layer control label refresh.

### Frontend — Theming

A `useTheme` composable (`src/composables/useTheme.ts`) manages the color mode as a singleton:

- Initialised in `main.ts` before `app.mount()` — prevents flash of wrong theme on load.
- Writes `data-theme="light"` or `data-theme="dark"` on `<html>`.
- **Auto** mode removes the attribute entirely — `app.css` uses `@media (prefers-color-scheme: light)` on `:root:not([data-theme])` to resolve the OS preference.
- All UI colors in `app.css` are CSS custom properties defined on `:root` (dark default) and overridden by `[data-theme="light"]`.
- The Settings modal uses `:global([data-theme="light"])` selectors to flip its glassmorphism card from white-on-dark to dark-on-light.

### Frontend — Phase Visibility

`refreshLayerVisibility()` in `labels.ts` was a chain of ad-hoc booleans. Replaced with a `PHASE_VISIBILITY` lookup table:

```typescript
const PHASE_VISIBILITY: Record<string, ReadonlySet<string>> = {
    areas:           new Set(['areas']),
    districts:       new Set(['areas', 'districts']),
    // …
}
```

This also fixed a bug: `publicBuildings` was incorrectly shown during the `publicSpaces` phase.

### Frontend — Edit Phase Guards

`editGeometry` previously had no phase guard — any visible feature could be reshaped from any phase. `editFeatureInfo` only blocked the `areas-in-districts` edge case. Both now enforce a consistent guard:

```
feature.phaseKey !== currentPhaseKey → alert + return
```

### Frontend — Naming Panel Colors

Naming panels are now coloured by their source feature type rather than a single purple:

| Source | Color |
|---|---|
| Districts | `#f39c12` orange |
| Roads | `#3498db` blue |
| Public Buildings | `#e67e22` burnt orange |
| Public Spaces | `#2ecc71` green |

### Frontend — Vite / Build

Migrated from `rollupOptions` to `rolldownOptions` (Vite 8 uses the Rolldown bundler). Manual chunks defined for `vendor-geoman`, `vendor-leaflet`, `vendor-turf`, and `vendor-graphology`. The `outDir` is set to `../NARS/wwwroot` so `npm run build` deploys directly to the backend. Static files (`login.html`, `login.css`, `NARS.jpg`) live in `public/` and are copied to `outDir` automatically.

### Backend — ScatteredAreaService

The fire-and-forget scattered area recomputation was extracted from `FeaturesController` into `IScatteredAreaService` / `ScatteredAreaService`. Registered as `AddScoped` in `Program.cs`. The controller receives it by constructor injection.

### Backend — Startup

`MigrateAsync()` replaced with `CanConnectAsync()`. The schema is managed via SQL scripts — not EF migrations — so `MigrateAsync` would fail querying `__EFMigrationsHistory`. `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` is applied at the top of `Program.cs` to match the existing `timestamp without time zone` columns.

### Database — Split Feature Tables

The monolithic `features` table (projected to 31+ million rows) has been replaced by 8 dedicated tables:

| Table | Feature type | Projected rows |
|---|---|---|
| `areas` | Urban areas + scattered | ~5 000 |
| `districts` | District polygons | ~50 000 |
| `city_centers` | City center circles | ~5 000 |
| `roads` | Road polylines | ~1 000 000 |
| `house_entrances` | Main + secondary entrances | ~30 000 000 |
| `public_buildings` | Public building polygons | ~100 000 |
| `public_spaces` | Gardens and squares | ~100 000 |
| `naming_panels` | Auto-generated signage | session-only |

Each table keeps `data jsonb` — the frontend payload is unchanged, zero frontend changes required.

**`feature_registry(id, feature_type)`** — written on every INSERT. `PUT /api/update/:id` and `DELETE /api/delete/:id` do a single PK lookup here to find which table to target in O(1).

**Global `feature_id_seq`** — a single PostgreSQL sequence shared by all tables so IDs are globally unique.

**`house_entrances.road_id`** — extracted from `data->>'roadDbId'` into a proper `bigint` column with a partial index. The road-side query (`WHERE road_id = @rid AND layer = 'main_entrance'`) no longer casts 30 million JSONB fields.

**Indexes added:**

- `ix_areas_user_id`, `ix_areas_user_layer`
- `ix_districts_user_id`, `ix_districts_data_gin` (GIN)
- `ix_roads_user_id`, `ix_roads_user_layer`, `ix_roads_data_gin` (GIN)
- `ix_house_entrances_user_id`, `ix_house_entrances_user_layer`, `ix_house_entrances_road_id` (partial)
- `ix_communes_boundaries_geometry` (GIST)

**Migration:** `move_features.sql` moves all rows from the old `features` table into the new split tables in a single transaction with a verification step, then deletes the source rows.

---

## Drawing UX

- **Auto-start on phase entry** — draw mode activates immediately when a phase is entered.
- **Click-to-draw fallback** — if draw mode is interrupted (ESC, context menu, alert), the next map click restarts it automatically.
- **Right-click cancels** — right-clicking anywhere cancels active draw or edit mode. If neither is active, the context menu opens.
- **ESC** — cancels draw mode; next click restarts it.
- **Phase guards** — editing (info or geometry) and removing a feature is only permitted in the feature's own phase. Attempting it from another phase shows an alert directing the operator to the correct phase.
- **No Geoman toolbar** — all draw controls are programmatic.

---

## Snapping

Geoman's built-in snap is disabled (`snappable: false`). Custom snapping in `snapping.ts`:

- **Endpoint snap** — snaps to existing road/polygon endpoints within 20 px.
- **City center perimeter snap** — snaps to the circle edge in pixel space:

$$\hat{d} = \frac{P_{cursor} - P_{center}}{\|P_{cursor} - P_{center}\|}$$

$$P_{snap} = P_{center} + \hat{d} \cdot r_{px}, \quad d_{snap} = \left| \|P_{cursor} - P_{center}\| - r_{px} \right|$$

The snap activates when $d_{snap} \leq 20\text{ px}$.

---

## Road Direction Algorithm

Located in `road-directions.ts`. Uses Turf.js for spatial math and Graphology for network traversal.

**Rules:** Roads orient away from the city center outward. Without a city center: North→South if more vertical, East→West if more horizontal. Dead-ends always flow from the connected endpoint to the free tip.

**Steps:** Network graph construction (O(n²), detects endpoint-to-endpoint, T-junction, X-junction) → DFS orientation from city center seed nodes → majority vote per split road → dead-end correction → coordinate flip + DB persist.

---

## House Entrance Numbering Algorithm

Located in `house-entrances.ts` (`setHouseNumbers`) and `create-handler.ts` (`pm:create` handler).

**Placement** — entrance type is derived automatically from the active reference. The server determines road side (`POST /api/road-side`) driving odd (left) / even (right) parity. BIS numbers are assigned at placement: $\text{BIS}_n = k + 1$.

**Assignment** — each marker is projected onto the road polyline, sorted by arc-length, then numbered by parity continuing from the highest already-assigned number on that road.

---

## Naming Panels

- Districts: panel at every polygon vertex (excluding the duplicate closing vertex).
- Roads: panel at start, end, and every 100 m along the polyline.
- Public Buildings / Spaces: panel at the first drawn vertex.
- Dedupe radius: 3 m — skip if an existing panel is closer.
- Panel color matches the source feature's phase color for visual association.

---

## Settings

The Settings modal (glassmorphism design matching the login page) provides:

- **Language** — English / French / Arabic. `fr` and `ar` locales are lazy-loaded. RTL layout applied automatically for Arabic.
- **Theme** — Light / Dark / Auto. Auto mode follows the OS preference via `@media (prefers-color-scheme)`. Selection persisted in `localStorage` under `nars_theme`.
- **Account** — update username, email, and password.
- **Feature types** — register custom feature type extensions.

---

## Dependencies

| Package | Purpose |
|---|---|
| `leaflet` | Map rendering |
| `@geoman-io/leaflet-geoman-free` | Draw / edit tools |
| `@turf/turf` | Spatial math |
| `graphology` + `graphology-traversal` | Road network graph |
| `vue` | Reactive UI |
| `vue-i18n` | Internationalisation (v10, Composition API) |
| `@vueuse/core` | Vue composable utilities |

---

## Development

```bash
# Frontend (Vite dev server — hot reload)
cd nars-vite
npm install
npm run dev        # http://localhost:5173
npm run build      # Compiles directly into ../NARS/wwwroot

# Backend (API server)
cd NARS
dotnet run         # http://localhost:5000
```

The Vite dev server proxies `/api/*` to the ASP.NET backend so both can run simultaneously during development.

**Production build workflow:**

```bash
cd nars-vite
npm run build      # → ../NARS/wwwroot (empties and rewrites)
# Restart backend or refresh — new assets served immediately
```

---

## Database Setup

```bash
# Fresh install
psql -U <user> -d <dbname> -f nars_db_v2.sql

# Migrating from Pre-Alpha (old features table still on disk)
psql -U <user> -d <dbname> -f nars_db_v2.sql
psql -U <user> -d <dbname> -f move_features.sql
```

`move_features.sql` runs in a single transaction and prints a row-count verification report before committing. The old `features` table is emptied (not dropped) — drop it manually once the application is verified.

---

## Known Constraints

- Road turn validation (≤ 90°) is enforced server-side in `ValidationController.cs`.
- City center radius defaults to 50 m if not stored.
- `timestamp without time zone` is used for `created_at` / `updated_at` columns. `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` is set in `Program.cs` to match. Migrate to `timestamptz` with `ALTER TABLE … ALTER COLUMN … TYPE timestamptz USING … AT TIME ZONE 'UTC'` when ready, then remove the switch.
- Schema is managed via SQL files — not EF migrations. `CanConnectAsync()` is used at startup instead of `MigrateAsync()`.
