# NARS — National Addressing Reference System

A full-stack GIS web application for Algerian municipal addressing.  
**Stack:** ASP.NET Core 10 · Vue 3 · TypeScript · Vite · Leaflet · Leaflet-Geoman 2.19.2 · Turf.js · Graphology

---

## Overview

NARS guides operators through 8 sequential mapping phases to produce a complete, standards-compliant address reference for a municipality. Each phase builds on the previous — areas before districts, districts before roads, roads before house entrances, and so on.

---

## Project Status

- Pre-Alpha stage completed. All 8 mapping phases are implemented end-to-end.

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
│   ├── naming-panels.ts    # Auto-generate naming panels from features (display-only)
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
- **City center perimeter snap** — snaps the cursor to the circle edge in pixel space:

$$\hat{d} = \frac{P_{cursor} - P_{center}}{\|P_{cursor} - P_{center}\|}$$

$$P_{snap} = P_{center} + \hat{d} \cdot r_{px}$$

$$d_{snap} = \left| \|P_{cursor} - P_{center}\| - r_{px} \right|$$

where $P$ are pixel-space coordinates and $r_{px}$ is the circle's rendered radius in pixels. The snap activates when $d_{snap} \leq 20\text{ px}$.

---

## City Center Phase

- Drawn as `L.Circle` (red, semi-transparent fill). Stored with `lat`, `lng`, `radius` in the database.
- **One city center per urban area** — placing a second circle inside the same area is blocked; the layer is discarded with an alert.
- **No dialog** — draw mode starts immediately on phase entry.
- **Phase entry guard** — if every urban area already has a city center, the phase renders read-only and draw mode is not activated.

---

## Road Direction Algorithm

Located in `road-directions.ts`. Uses **Turf.js** for spatial math and **Graphology** for network traversal.

### Rules

1. **With city center** — all roads are oriented away from the city center outward to the municipal boundary. Geographic fallback is never used.
2. **Without city center** — geographic fallback: North → South if more vertical, East → West if more horizontal.
3. **Dead-end roads** — always flow FROM the connected endpoint TO the free tip, regardless of network orientation.

### Step 1 — Network Graph Construction (`buildConnectionGraph`)

A single **O(n²)** pass over all roads builds a Graphology undirected multigraph. For each road pair the following junction types are detected:

**Endpoint-to-endpoint** — two road endpoints within merge radius $\varepsilon = 30\text{ m}$ are collapsed into a shared graph node via `resolveNode`:

$$\text{merge}(A, B) \iff d(A, B) \leq \varepsilon$$

**T-junction** — endpoint $E$ of road $B$ lands on the body of road $A$. Detected by nearest-point projection:

$$P^* = \underset{P \in A}{\arg\min}\; d(E, P)$$

A junction is recorded when $d(E, P^*) \leq \varepsilon$, and road $A$ is split at $P^*$.

**X-junction** — two road bodies cross. The intersection point $X = A \cap B$ is computed via `turf.lineIntersect`. Both roads are split at $X$ when $d(X, \text{endpoint}) > \varepsilon$.

Split points along each road are sorted by their fractional position $t \in [0, 1]$ and applied in a single pass, producing an ordered list of sub-segments. Each sub-segment is added as a graph edge:

$$G = (V, E), \quad V = \{\text{junction points}\}, \quad E = \{\text{road sub-segments}\}$$

### Step 2 — Orientation

**With city center** — recursive DFS from the city center perimeter outward.

Seed nodes are all graph nodes $v$ satisfying:

$$\bigl| d(v, C) - r \bigr| \leq \varepsilon$$

where $C$ is the city center coordinates and $r$ its radius. For each seed, `orientFrom` recurses through the graph sorting neighbors by their straight-line distance to the city center:

$$\text{sort neighbors by } d(v_i, C) \text{ ascending}$$

A segment $s$ with endpoints $A, B$ is oriented so that the end **closer to the city center** is the start:

$$s.\text{reversed} \iff d(s.\text{coords}[0],\; \text{fromNode}) > d(s.\text{coords}[\text{last}],\; \text{fromNode})$$

A `visitedRoads` set ensures first-reached assignment is final — no road is re-oriented by a longer path.

**Without city center** — geographic fallback per segment:

$$\text{direction} = \begin{cases} \text{North} \to \text{South} & \text{if } |\Delta\text{lat}| \geq |\Delta\text{lng}| \\ \text{East} \to \text{West} & \text{otherwise} \end{cases}$$

### Step 3 — Majority Vote

Each original road $R$ may have been split into $k$ sub-segments. The final orientation is determined by majority vote over the sub-segment orientations:

$$\text{orient}(R) = \begin{cases} \text{forward} & \text{if } |\{s_i : s_i.\text{reversed} = \text{false}\}| \geq \tfrac{k}{2} \\ \text{reversed} & \text{otherwise} \end{cases}$$

### Step 4 — Dead-End Correction

After the vote, each road's original endpoints are located in the graph. Let $\deg(v)$ denote the degree of node $v$:

- If $\deg(v_{\text{first}}) = 1$ and $\deg(v_{\text{last}}) > 1$ → orient FROM $v_{\text{last}}$ (connected side).
- If $\deg(v_{\text{first}}) > 1$ and $\deg(v_{\text{last}}) = 1$ → orient FROM $v_{\text{first}}$.
- If $\deg(v_{\text{first}}) = \deg(v_{\text{last}}) = 1$ (isolated road split by a T-junction on its body) → orient FROM the endpoint closer to the city center:

$$\text{from} = \underset{v \in \{v_{\text{first}},\; v_{\text{last}}\}}{\arg\min}\; d(v, C)$$

### Step 5 — Apply

Reversed roads have their coordinate array flipped, the Leaflet polyline redrawn, and the change persisted to the database via `PUT /api/update/:id`. Endpoint direction arrows are then rebuilt for all roads.

---

## House Entrance Numbering Algorithm

Located in `context-menu.ts` (`setHouseNumbers`) and `map/index.ts` (`pm:create` handler).

### Placement (no modal)

When the operator places a marker in Phase 4, the entrance type and road/entrance assignment are determined automatically from the **active reference**:

| Active reference | Entrance type created |
|------------------|-----------------------|
| Reference road set | Main entrance (`main_entrance`) |
| Reference main entrance set | Secondary entrance (`secondary_entrance`) |

The reference entrance takes priority over the reference road — if both are set, a secondary entrance is created.

**Side detection** — for main entrances, the server determines which side of the road the marker falls on (`POST /api/road-side`). The result drives parity:

$$\text{side} = \begin{cases} \text{left} & \Rightarrow \text{odd number} \\ \text{right} & \Rightarrow \text{even number} \end{cases}$$

The marker is saved immediately with label `?` (number deferred).

**BIS numbering** — secondary entrances are numbered at placement. Given $k$ existing secondary entrances linked to the same main entrance:

$$\text{BIS}_{n} = k + 1, \quad \text{label} = \texttt{"BIS"} + \text{zero-pad}(n, 2)$$

### Number Assignment (`setHouseNumbers`)

Triggered by the operator via right-click → **Set House Numbers** on the map background. Operates only on unassigned markers (`label = ?`) belonging to the reference road.

**Step 1 — Project onto road.** Each entrance marker $M_i$ is projected onto the road polyline $\mathcal{L}$ using nearest-point-on-line:

$$P_i^* = \underset{P \in \mathcal{L}}{\arg\min}\; d(M_i, P)$$

$$\ell_i = \text{arc-length from } \mathcal{L}[0] \text{ to } P_i^*$$

**Step 2 — Sort by position along road:**

$$M_{\sigma(1)},\; M_{\sigma(2)},\; \ldots,\; M_{\sigma(m)} \quad \text{where } \ell_{\sigma(1)} \leq \ell_{\sigma(2)} \leq \cdots \leq \ell_{\sigma(m)}$$

**Step 3 — Assign numbers by parity.** Odd and even sequences are independent counters, continuing from the highest number already assigned on that road:

$$n_{\text{odd},\; 0} = \max\bigl(\{n \in \text{assigned} : n \bmod 2 = 1\}\bigr) + 2 \quad (\text{or } 1 \text{ if none})$$
$$n_{\text{even},\; 0} = \max\bigl(\{n \in \text{assigned} : n \bmod 2 = 0\}\bigr) + 2 \quad (\text{or } 2 \text{ if none})$$

For each entrance in sorted order:

$$\text{number}(M_{\sigma(j)}) = \begin{cases} n_{\text{odd}} & \text{if side} = \text{left},\quad n_{\text{odd}} \mathrel{+}= 2 \\ n_{\text{even}} & \text{if side} = \text{right},\quad n_{\text{even}} \mathrel{+}= 2 \end{cases}$$

Icons are updated immediately and all changes are persisted to the database in parallel.

---

## Naming Panels Mathematics

- Districts: place at every distinct polygon vertex (excluding the duplicate closing vertex when equal to the first).
- Roads: place at start, end, and every S meters along the polyline (S = 100 m).
- Public Buildings/Spaces: place at the first drawn vertex of the polygon.
- Dedupe within radius r (r = 3 m): do not add a marker if an existing naming panel lies closer than r.

Road station interpolation for a segment [A, B] with accumulated length acc and target nextAt:

Let segLen = d(A, B). While acc + segLen ≥ nextAt, compute t = (nextAt − acc) / segLen and:

$\displaystyle P=\bigl(A_{lat} + (B_{lat}-A_{lat})\,t,\; A_{lng} + (B_{lng}-A_{lng})\,t\bigr)$

Append P, then increase nextAt by S and continue across segments. Always include the first and last vertices as stations.

Dedupe check for a candidate point C against existing panels E:

$\displaystyle \exists\, p \in E:\ d(p, C) < r \;\Rightarrow\; \text{skip}$

## Context Menu — Phase 4 (House Entrances)

The right-click menu is context-sensitive in Phase 4:

| Right-clicked feature | Available actions |
|-----------------------|-------------------|
| Road | 📍 Set as Reference Road / ❌ Remove Reference Road |
| Main entrance marker | 📍 Set as Reference Entrance · ⬟ Edit Geometry · 🗑️ Remove |
| Secondary entrance marker | ⬟ Edit Geometry · 🗑️ Remove |
| Map background | 🔢 Set House Numbers |

Roads in Phase 4 are read-only — no Edit Info, no Edit Geometry, no Remove.

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

- Road turn validation (≤ 90°) is enforced server-side in `ValidationController.cs`.
- City center radius defaults to 50 m if not stored.
