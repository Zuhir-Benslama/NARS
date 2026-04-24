# NARS — National Addressing Reference System

A full-stack GIS web application for Algerian municipal addressing.  
**Stack:** ASP.NET Core 10 · Vue 3 · TypeScript · Vite 8 · **Maplibre GL JS** · Maplibre-Geoman 0.7.1 · Turf.js · Graphology · vue-i18n 10 · @vueuse_core · maplibre-rotate

**NARStreet (Mobile):** Kotlin · Jetpack Compose · MapLibre Native 11.x · MapLibre-Geoman (Android)

---

## Project Status

**Version 1.1-Beta** — All 8 mapping phases fully operational. Greatest migration from Leaflet to Maplibre GL JS complete. Approaching 1.0-Stable.

> **NARStreet** — Android/Kotlin companion app in active development (MapLibre Native 11.x).

> ⚠️ **Known Issue:** MapLibre Native fails to render text labels for NARStreet when using Arabic/RTL scripts. The default glyphs endpoint (`demotiles.maplibre.org`) does not include Noto Sans Arabic fonts. Text labels appear blank or missing on the map. Fix requires hosting custom glyphs with Arabic font support or using MapLibre GL JS (web) which loads fonts differently.

---

## Mapping Phases

| # | Key | Label | Draw Type | Status | Description |
|---|-----|-------|-----------|--------|-------------|
| 0 | `areas` | Areas | Polygon | ✅ Done | Urban areas (Main Urban, Secondary Urban). Scattered areas computed automatically. Main urban name = commune name. |
| 1 | `districts` | Districts | Polygon | ✅ Done | Districts inside urban areas. Must share edges — no gaps. |
| 2 | `cityCenter` | City Center | Circle | ✅ Done | City center circle per urban area. Drives road direction and entrance numbering. |
| 3 | `roads` | Roads | Polyline | ✅ Done | Roads inside the municipal limit. Must connect to at least one other road. |
| 4 | `houseEntrances` | House Entrances | Marker | ✅ Done | Main entrances (left = odd, right = even) and secondary entrances (BIS01, BIS02…). |
| 5 | `publicBuildings` | Public Buildings | Polygon | ✅ Done | Public buildings. Allowed everywhere including scattered areas. |
| 6 | `publicSpaces` | Public Spaces | Polygon | ✅ Done | Gardens and squares inside the municipal limit. |
| 7 | `namingPanels` | Naming Panels | Marker | ✅ Done | Auto-generated signage panels. Display-only — no manual placement. Grab cursor. |

### Phase Visibility Matrix

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
│   ├── index.ts             # Map orchestrator — initMap, phase navigation
│   ├── draw-control.ts      # buildDrawControl, updateLayerEditability
│   ├── draw-events.ts       # All pm:* and Leaflet map event wiring
│   ├── create-handler.ts    # pm:create business logic
│   ├── loader.ts            # loadFromDatabase, loadUserAndCommune
│   ├── context-menu.ts      # Right-click menu, bindContextMenu
│   ├── edit-actions.ts      # removeFeature, editGeometry, editFeatureInfo
│   ├── house-entrances.ts   # setReferenceRoad/Entrance, setHouseNumbers
│   ├── rotation.ts          # Map rotation controls (↺ ↻)
│   ├── export.ts            # PDF export (html2canvas + jsPDF)
│   ├── state.ts             # Shared ctx object
│   ├── snapping.ts          # Custom vertex snapping
│   ├── road-directions.ts   # Road direction algorithm
│   ├── features.ts          # buildFeatureData, saveToDatabase
│   ├── labels.ts            # Labels, PHASE_VISIBILITY table
│   ├── geometry.ts          # Spatial helpers
│   ├── naming-panels.ts     # Auto-generate naming panels
│   └── styles.ts            # Phase-specific Leaflet styles
├── components/
│   ├── PhaseBar.vue         # Phase navigation bar
│   ├── FeatureModal.vue     # Feature creation / edit dialog
│   ├── InfoPanel.vue        # Feature counts panel (top-right)
│   ├── ProfileMenu.vue      # User profile + settings entry
│   ├── SettingsModal.vue    # Language, theme, account, export
│   └── TileControl.vue      # Custom tile layer switcher (bottom-right)
├── composables/
│   └── useTheme.ts          # Theme singleton (light / dark / auto)
├── locales/
│   ├── en.json              # English
│   ├── fr.json              # French
│   └── ar.json              # Arabic (RTL)
├── store.ts                 # Reactive Vue store
├── phases.ts                # Phase definitions, feature type hierarchy
├── types.ts                 # TypeScript interfaces
├── i18n.ts                  # vue-i18n instance, setLang
├── api.ts                   # apiFetch wrapper
└── app.css                  # All styles + CSS variable theme system
```

---

## What Changed in v1.1-Beta

### Map Controls

**Rotation** — `↺` and `↻` buttons at bottom-right rotate the map 5° per click via `leaflet-rotate`. Implemented in `rotation.ts` as a Leaflet control. The map container is moved outside `leaflet-rotate`'s DOM wrapper so pointer events on all controls work correctly.

**Tile layer switcher** — native Leaflet layer control replaced by `TileControl.vue` (custom Vue component, bottom-right). Immune to `leaflet-rotate` DOM interference. Four base layers: Satellite (ESRI), Street (OSM), Light (CARTO), Dark (CARTO). `window.__narsSetBaseLayer(key)` swaps layers from `index.ts`.

**Info panel** — top-right below profile menu, width matches profile button.

### Phase 8 — Naming Panels

All Geoman modes fully disabled (`pmIgnore: true`). Grab cursor set on phase entry and restored on exit. Panels auto-generated on first entry; labels hidden for source layers. Per-source colors: Districts `#f39c12` · Roads `#3498db` · Buildings `#e67e22` · Spaces `#2ecc71`.

### PDF Export

Export tab in Settings — A3 or A0 landscape. `html2canvas` captures the live `#map` element (controls detached before capture, restored after). ESRI satellite swaps to OSM during capture to avoid CORS canvas tainting. `jsPDF` embeds the canvas with a title bar. Animated progress bar with step labels.

### Theming

Full CSS variable system (`--modal-card-bg`, `--text-primary`, etc.) across `:root` (dark default), `[data-theme="light"]`, and `@media (prefers-color-scheme: light)`. Info panel, profile menu, dropdown, tile control, settings modal, and Leaflet controls all theme-aware.

### Feature Modal

- **Main urban area** — label disabled, pre-filled with commune name (`store.municipalityName` → `store.user.commune.name_fr` fallback). Commune name always used on submit.
- **Secondary urban area** — label editable.
- **Hint** — rendered via `t(phase.hint)` (was showing raw i18n key).
- **Validation** — label "Required" skipped for `central_urban` and `cityCenter`.

### Edit Phase Guards

Every edit (info, geometry) and delete checks that the target feature belongs to the current active phase. Wrong-phase attempts show a localised alert.

---

## Drawing UX

- Auto-start draw on phase entry
- Click-to-draw fallback after ESC / context menu / alert
- Right-click cancels draw or edit, or opens context menu
- Phase guards on all edit and delete actions
- Naming panels phase: grab cursor, all drawing/editing blocked

---

## Road Direction Algorithm

`road-directions.ts` — Turf.js + Graphology. Builds O(n²) network graph detecting endpoint-to-endpoint, T-junction, and X-junction connections. DFS orientation from city center outward → majority vote per split road → dead-end correction → coordinate flip + DB persist.

---

## House Entrance Numbering

`house-entrances.ts` — projects each marker onto the road polyline, sorts by arc-length, assigns odd (left) / even (right) numbers continuing from the highest already assigned. BIS numbering for secondary entrances. All changes persisted in parallel.

---

## Naming Panels

Districts: every vertex · Roads: start + end + every 100 m · Buildings/Spaces: first vertex · Dedupe radius 3 m · Color matches source phase.

---

## Settings Modal

| Tab | Contents |
|---|---|
| General | Language (EN/FR/AR), Theme (Light/Dark/Auto) |
| Account | Username, email, password |
| Export | Paper size (A3/A0), download PDF |
| Feature types | Custom type extensions |
| About | Version info |

---

## Dependencies

| Package | Purpose |
|---|---|
| `maplibre-gl` | Map rendering |
| `maplibre-geoman` | Draw / edit tools |
| `@turf/turf` | Spatial math |
| `graphology` + `graphology-traversal` | Road network graph |
| `vue` | Reactive UI |
| `vue-i18n` | Internationalisation v10 |
| `@vueuse/core` | Vue composable utilities |
| `leaflet-rotate` | Map bearing rotation |
| `html2canvas` | Map capture for PDF export |
| `jspdf` | PDF generation |

---

## NARStreet — Android Companion App

Native Android app (Kotlin + Jetpack Compose) using MapLibre Native 11.x + MapLibre-Geoman for field data collection. Mirrors NARS web workflow on mobile.

### Architecture

```
app/src/main/java/com/nars/maplibre/
├── MainActivity.kt              # Entry point
├── NarsViewModel.kt             # UI state management
├── data/
│   ├── api/ApiClient.kt         # REST client
│   ├── model/                   # NarsFeature, FeatureTypes, User, etc.
│   └── repository/              # FeatureRepository, LocalFeatureRepository
├── modes/NarsGeoman.kt         # Drawing mode integration
├── ui/
│   ├── components/             # PhaseBar, InfoPanel, FeatureModal, etc.
│   ├── screens/                # MapScreen, LoginScreen, SettingsScreen
│   └── theme/Theme.kt          # Material 3 theming
└── utils/                      # RoadDirectionsCalculator, HouseNumberingManager, etc.
```

### Known Issue: Text Rendering with Arabic/RTL

MapLibre Native's `SymbolLayer.textField` requires glyphs from a PBF font endpoint. The default `demotiles.maplibre.org` glyphs only include Latin characters — Arabic text (including district/road names in Arabic) renders as blank/missing.

**Workaround (in progress):** Host custom glyphs with Noto Sans Arabic bundled, or use web-based MapLibre GL JS which handles fonts differently.

### Build

```bash
cd NARStreet && ./gradlew assembleDebug
```

---

## Development

```bash
cd nars-vite && npm install
npm run dev        # http://localhost:5173
npm run build      # → ../NARS/wwwroot

cd NARS && dotnet run   # http://localhost:5000
```

---

## Database Setup

```bash
psql -U <user> -d <dbname> -f nars_db_v2.sql        # fresh install
psql -U <user> -d <dbname> -f move_features.sql      # migrate from Pre-Alpha
```

### Split Feature Tables

| Table | Type | Scale |
|---|---|---|
| `areas` | Urban + scattered | ~5 000 |
| `districts` | District polygons | ~50 000 |
| `city_centers` | City center circles | ~5 000 |
| `roads` | Road polylines | ~1 000 000 |
| `house_entrances` | Main + secondary entrances | ~30 000 000 |
| `public_buildings` | Public buildings | ~100 000 |
| `public_spaces` | Gardens and squares | ~100 000 |
| `naming_panels` | Auto-generated panels | session-only |

`feature_registry(id, feature_type)` — O(1) routing for PUT/DELETE. Global `feature_id_seq` — unique IDs across all tables. `house_entrances.road_id` extracted column for indexed road-side queries.

---

## Known Constraints

- Road turn ≤ 90° validated server-side in `ValidationController.cs`
- ESRI satellite tiles swap to OSM during PDF export (CORS)
- Schema managed via SQL — `CanConnectAsync()` at startup, not `MigrateAsync()`
- `Npgsql.EnableLegacyTimestampBehavior = true` until columns migrate to `timestamptz`

---

## Roadmap

| Version | Status |
|---|---|
| 1.1-Alpha | ✅ Released |
| **1.1-Beta** | ✅ Current |
| 1.0-Stable | 🔜 Final QA + edge case hardening |
| **NARStreet** | 📱 Android/Kotlin app (MapLibre Native 11.x) — Text rendering issue with Arabic/RTL fonts |
