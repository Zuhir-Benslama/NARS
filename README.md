# NARS — National Addressing Reference System

A full-stack GIS web application for Algerian municipal addressing.  
**Stack:** ASP.NET Core 10 · Vue 3 · TypeScript · Vite · Leaflet · Leaflet-Geoman 2.19.2 · Turf.js · Graphology

---

## Overview

NARS guides operators through 7 sequential mapping phases to produce a complete, standards-compliant address reference for a municipality. Each phase builds on the previous — areas before districts, districts before roads, roads before house entrances, and so on.

---

## Mapping Phases

| # | Key | Label | Draw Type | Description |
|---|-----|-------|-----------|-------------|
| 0 | `areas` | Areas | Polygon | Urban areas (Main Urban, Secondary Urban). Scattered areas computed automatically. |
| 1 | `districts` | Districts | Polygon | Districts inside urban areas. Must share edges — no gaps allowed. |
| 2 | `cityCenter` | City Center | Circle | City center circle per urban area. Determines road direction and house entrance numbering. |
| 3 | `roads` | Roads | Polyline | Roads inside the municipal limit. Must connect to at least one other road. |
| 4 | `houseEntrances` | House Entrances | Marker | Main entrances (left = odd, right = even) and secondary entrances (BIS01, BIS02…). |
| 5 | `publicBuildings` | Public Buildings | Polygon | Public buildings. Allowed everywhere including scattered areas. |
| 6 | `publicSpaces` | Public Spaces | Polygon | Public spaces (gardens, squares) inside the municipal limit. |

---

## Architecture

```
src/
├── map/
│   ├── index.ts            # Map orchestrator — phase setup, draw events, pm:create handler
│   ├── state.ts            # Shared ctx object (map, drawnItems, layers) — no circular imports
│   ├── context-menu.ts     # Right-click menus for map background and features
│   ├── snapping.ts         # Custom vertex snapping (endpoint + city center perimeter)
│   ├── road-directions.ts  # Road direction algorithm (see below)
│   ├── features.ts         # buildFeatureData, saveToDatabase, applyStyle
│   ├── labels.ts           # Permanent labels, edge labels, endpoint arrows
│   ├── geometry.ts         # Spatial helpers
│   └── styles.ts           # Phase-specific Leaflet style objects
├── components/
│   ├── PhaseBar.vue        # Phase navigation bar
│   ├── FeatureModal.vue    # Feature creation / edit dialog
│   ├── InfoPanel.vue       # Sidebar info panel
│   └── ProfileMenu.vue     # User profile menu
├── store.ts                # Reactive Vue store (AppStore)
├── phases.ts               # Phase definitions, area/district/road/space types
├── types.ts                # TypeScript interfaces (AppStore, Phase, LayerEntry, …)
├── api.ts                  # apiFetch wrapper
└── validation.ts           # Client-side validation helpers
```

---

## Drawing UX

- **Auto-start on phase entry** — draw mode activates immediately when a phase is entered (no toolbar click needed).
- **Click-to-draw fallback** — if draw mode is interrupted (ESC, context menu, alert), the next map click restarts it automatically.
- **Right-click cancels** — right-clicking anywhere (map background or feature) cancels active draw or edit mode. If neither is active, the context menu opens.
- **ESC** — cancels draw mode; next click restarts it.
- **No Geoman toolbar** — all draw controls are programmatic; the Geoman toolbar is hidden.

---

## Snapping

Geoman's built-in snap is disabled (`snappable: false` passed to every `pm.enableDraw` call). Custom snapping is implemented in `snapping.ts`:

- **Endpoint snap** — snaps to existing road/polygon endpoints within 20 px.
- **City center perimeter snap** — snaps to the circle edge using pixel-space geometry: `snap point = center + (cursor − center) / |cursor − center| × radiusPx`. Snap distance = `|cursorDist − radiusPx|`.

---

## City Center Phase

- Drawn as `L.Circle` (red, semi-transparent fill). Stored with `lat`, `lng`, `radius` in the database.
- **One city center per urban area** — placing a second circle inside the same area is blocked; the layer is discarded with an alert.
- **No dialog** — draw mode starts immediately on phase entry. The old confirmation dialog has been removed.
- **Phase entry guard** — if every urban area already has a city center, the phase renders read-only and draw mode is not activated.

---

## Road Direction Algorithm

Located in `road-directions.ts`. Uses **Turf.js** for spatial math and **Graphology** for network traversal.

### Rules

1. **With city center** — all roads are oriented away from the city center outward to the municipal boundary. Geographic fallback is never used.
2. **Without city center** — geographic fallback: North → South if more vertical, East → West if more horizontal.
3. **Dead-end roads** — always flow FROM the connected endpoint TO the free tip, regardless of network orientation.

### Algorithm — Two Phases

#### Phase 1: Connection Scan (`buildConnectionGraph`)

Single O(n²) pass over all roads. For each road, scans every other road to detect:

- **Endpoint-to-endpoint** — handled by `resolveNode` merging endpoints within 30 m.
- **T-junction** — endpoint of road B lands on the body of road A (detected via `turf.nearestPointOnLine`). Road A is split at the junction point.
- **X-junction** — two road bodies cross (detected via `turf.lineIntersect`). Both roads are split at the intersection.

Split points are sorted along each road and applied in one pass — no re-looping. Result: a Graphology graph where every node is a connection point and every edge is a road sub-segment (`Seg`).

#### Phase 2: Orientation (Recursive DFS)

**With city center:**

1. The city center's radius is used to find all graph nodes that snapped to the circle perimeter (within 30 m of the perimeter).
2. Pre-compute straight-line distance from city center to every graph node (`distToCC`).
3. Recursive DFS (`orientFrom`) from each perimeter seed:
   - Neighbors are sorted by `distToCC` — closest roads oriented first.
   - Each edge: `seg.reversed = coords[0]` is farther from `fromNode` than `coords[last]`.
   - `visitedEdges` (edge keys) + `visitedRoads` (dbIds) — once a road is assigned it cannot be re-assigned by any other path, guaranteeing shortest-path priority.
   - All sub-segments of an assigned road are bulk-marked visited immediately.

**Without city center:** `geographicDirection()` applied to every segment.

#### Phase 3: Majority Vote

Each original road may have multiple sub-segments (from splits). A majority vote (`fwd` vs `rev` count) determines the final orientation of the whole road.

#### Dead-End Correction (post-vote)

After the vote, checks each road's original endpoints against the graph:

- If one endpoint is degree 1 (free tip) and the other is degree > 1 (connected) → orient FROM the connected side.
- If **both** endpoints are degree 1 (road was split as a T-junction host, so both original tips became sub-segment ends) → use distance to city center to determine the from-side (closer = from).

#### Phase 4: Apply

Reversed roads have their `coordinates` array flipped, the Leaflet polyline updated, and the change persisted to the database via `PUT /api/update/:id`. Endpoint arrows are rebuilt for all roads.

---

## Dependencies

| Package | Purpose |
|---------|---------|
| `leaflet` | Map rendering |
| `@geoman-io/leaflet-geoman-free` | Draw / edit tools |
| `@turf/turf` | Spatial math (distance, nearest point on line, line intersect, line length) |
| `graphology` | Road network graph (nodes = endpoints, edges = segments) |
| `graphology-traversal` | BFS traversal utilities |
| `graphology-shortest-path` | Dijkstra (available, currently superseded by recursive DFS) |
| `vue` | Reactive UI |

---

## Development

```bash
npm install
npm run dev       # Vite dev server
npm run build     # Production build
```

Backend: ASP.NET Core 10. API base: `/api/`. Authentication via session cookie.

---

## Known Constraints

- Phases 4–6 (House Entrances, Public Buildings, Public Spaces) UI pending.
- Road turn validation (≤ 90°) is enforced server-side in `ValidationController.cs`.
- City center radius defaults to 50 m if not stored.
