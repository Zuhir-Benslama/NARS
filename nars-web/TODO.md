# NARS Web TODO

Code quality issues found during review of the Maplibre GL JS frontend
(Vue 3 + Pinia + TypeScript). Grouped by severity.

## Review round 1 (current)

Baseline verified before fixing: `npm run lint`, `npm run typecheck`,
`npm run lint:css`, `npm run build` and `npm audit` all clean; 932 unit tests
passing; coverage above the configured thresholds (60/52/62/61). No High
findings. All findings below fixed.

## Medium

- [x] **Coverage gaps in testable logic with no test file at all**
  - `src/components/WilayaDetailPage.vue` was **0%** — role-based branching
    (national/wilaya admin), slug resolution, abort handling, routing.
    ✅ **Done:** `src/components/WilayaDetailPage.test.ts` (8 tests).
  - `src/components/settings/SettingsFeatures.vue` was **33%** — add-custom-feature form.
    ✅ **Done:** `src/components/settings/SettingsFeatures.test.ts` (6 tests).
  - `src/components/settings/SettingsGeneral.vue` was **40%** — language/theme switcher.
    ✅ **Done:** `src/components/settings/SettingsGeneral.test.ts` (5 tests).
  - `src/components/settings/LocationSearchSelect.vue` was **27%** — debounced search,
    option extraction, dropdown, race handling.
    ✅ **Done:** `src/components/settings/LocationSearchSelect.test.ts` (8 tests).
  - `src/map/export.ts` was **0%** — `computeImageDimensions` and the PDF pipeline.
    ✅ **Done:** `src/map/export.test.ts` (7 tests) with html2canvas/jspdf mocked
    (regular-function constructor so `new jsPDF()` works; getter `default` export to
    exercise the missing-deps branch).
  - `src/map/house-entrances.ts` was **8.6%** — reference road/entrance selection.
    ✅ **Done:** `src/map/house-entrances.test.ts` (8 tests).

- [x] **`LocationSearchSelect.vue` out-of-order search race** (`src/components/settings/LocationSearchSelect.vue:90-108`)
  - `runSearch` had no superseding mechanism: a slow response for an older query
    could overwrite the results of a newer one, and in-flight requests were never
    invalidated on unmount.
  - ✅ **Done:** monotonic `searchGen` counter — a response is only applied when its
    generation is still current; `cleanup()` invalidates in-flight work. Regression
    test proves a late stale response is ignored.

## Low

- [x] **`.env.example` documentation gap** (`.env.example`)
  - `VITE_API_BASE` and `VITE_OTEL_CORS_URLS` are referenced in source
    (`src/config/index.ts`, `src/lib/telemetry.ts`) but were not documented.
  - ✅ **Done:** both documented with defaults.

- [x] **`_boundaryMap!` non-null assertion** (`src/map/map-boundary.ts:52`)
  - Non-null assertion in `onBoundaryClick`; guarded instead with an early return.

- [x] **Redundant `as string | undefined` cast** (`src/map/house-entrances.ts:32`)
  - `entry.data.geometry` is already `string | undefined` on every feature type;
    the cast was removed.

## Verification

- `npm run test:run` — 975 passed (was 932), 93 files.
- Coverage after fixes: statements 73.4%, branches 63.7%, functions 75.7%,
  lines 74.7% (all above thresholds).
- `npm run lint`, `npm run typecheck`, `npm run lint:css`, `npm run build`,
  `npm audit` — all clean.
- E2E suite (`npm run test:e2e`) — 16 Playwright specs under `e2e/`, run
  locally against the dev server with API calls mocked via `page.route`.

## Review round 2

Baseline verified before fixing: `npm run lint`, `npm run typecheck`,
`npm run lint:css` clean; 958 tests passing (92 files). Findings below, grouped
by severity.

## High

- [x] **Seed-ring tolerance in `orientFromCityCenter` compared km against meters**
  (`src/map/roads/road-orient.ts:80`)
  - `@turf/distance` defaults to kilometers but the filter compared the result
    against `CONNECT_M` (30 **meters**) as if it were 30 km — every node within
    ±30 km of the city-center ring became a seed, effectively seeding the whole
    dataset. The caller (`road-directions.ts:42`) passes the stored city-center
    radius, which is in meters (computed via `computeCircleRadius`).
  - ✅ **Done:** distance now requested with `{ units: "meters" }`, so both
    `radius` and `CONNECT_M` are meters. Tests updated to meter radii
    (1100 m / 1000 m / 100 km), comments corrected.

- [x] **`prepareModalExtras()` never actually ran — `mainUrbanExists` guard was dead**
  (`src/map/features/feature-modal.ts:6`, call site was `FeatureModal.vue` `onMounted`)
  - `FeatureModal` mounts once at app start (when `phaseIndex` is null), so the
    `onMounted` guard never fired. The areas modal therefore always offered the
    `central_urban` option (a duplicate main-urban area could be created) and
    road/main-entrance options were never populated. `draw-modal.ts`'s comment
    even promised the call.
  - ✅ **Done:** `openModalForFeature` now awaits `prepareModalExtras(phase)`
    after `openModal()` (create path only — `openEditModal` still untouched, so
    existing data is not clobbered). Dead `onMounted` block removed.

## Medium

- [x] **`switchBaseLayer` committed `currentActiveStyle` before the style loaded**
  (`src/map/map-init.ts`)
  - On style-load timeout/error the map kept the old style while
    `currentActiveStyle` already pointed at the new one, silently skipping later
    switches for that key.
  - ✅ **Done:** `currentActiveStyle` is only updated after `style.load`
    resolves; on failure it is reverted to the previous value.

- [x] **`WilayaDetailPage` loaded data only in `onMounted` — stale on slug change**
  (`src/components/WilayaDetailPage.vue`)
  - Navigating `/nars/<w1>` → `/nars/<w2>` reuses the instance and kept showing
    the previous wilaya's data.
  - ✅ **Done:** load extracted into a `load()` that resets state, aborts the
    prior request, and is driven by `watch(() => route.params.wilayaName, load,
    { immediate: true })`.

## Low

- [x] **Direct store-state mutation outside an action**
  (`src/map/snapping/snap-sources.ts:13`) — added `setSnapExclude` action to
  `snapStore` and used it.
- [x] **Untracked re-scheduled flush timer in logger** (`src/lib/logger.ts:75`) —
  `setTimeout` now assigned to `timer` so `push()`/`resetLoggerState`/`beforeunload`
  can't leave overlapping flush timers.
- [x] **Boundary popup never removed** (`src/map/map-boundary.ts:51`) — repeated
  clicks stacked unclosed popups; a single reusable popup is removed before
  re-adding.
- [x] **Double cast `as unknown as ModalResult`** (`src/map/features/loader-build.ts:22`)
  — reduced to `data as ModalResult` (typechecks fine; literal `type` unions match).

## Known / deferred

- **House-entrance modal path is unreachable** — `draw-modal.ts` returns a direct
  `ModalResult` for `houseEntrances` and `ctx-menu-actions.ts:71` blocks editing,
  so `FeatureModal`'s road-side/BIS watchers and `RoadAssignmentSelector` are dead
  code. Wiring the modal back up (and passing real geometry to `fetchRoadSide`)
  is a product decision — deferred.
- **`LocationSearchSelect` doesn't reflect `modelValue` on edit** — `query` is only
  written by typing/selecting, so editing a user shows blank wilaya/daira/commune
  fields. Needs an id→name resolution (backend or a label prop) — deferred.
- **`geoman-events.ts` `onEditEnd` casts `MultiLineString`/`MultiPolygon` coords as
  `[number, number][]`** (`MultiLineString` is nested arrays) — latent, roads are
  always `LineString`. Left for a future geometry refactor.
- **Hardcoded English strings in `RoadAssignmentSelector.vue`** — reachable only
  via the dead modal path (see above); move to the i18n catalog if that path is
  wired back up.

## Verification (round 2)

- `npm run test:run` — 958 passed (92 files), unchanged count.
- `npm run lint`, `npm run typecheck`, `npm run lint:css` — all clean.
- E2E suite not run locally (requires the full stack).

## Review round 3

All four deferred items were resolved (product decision: remove the dead code).
Round-1/round-2 E2E notes corrected — the suite runs locally.

## Fixed

- [x] **Dead house-entrance modal path removed** — product decision to remove the
  code instead of rewiring it. Deleted `RoadAssignmentSelector.vue` (+ its test),
  the `fetchRoadSide`/`computeBisNumber` helpers and the house-entrances branch of
  `prepareModalExtras`, the house-entrance branches in `useFeatureValidation`
  (`isHouseEntranceEdit`, validation/build branches), the `FeatureModal.vue`
  watchers/controller/template block, and the store state + actions
  (`entranceTypeKey`, `roadOptions`, `selectedRoadIdx`, `entranceSide`,
  `entranceNumber`, `entranceSideLoading`, `mainEntranceOptions`,
  `selectedMainIdx`, `bisNumber`, `setRoadOptions`, `setMainEntranceOptions`).
  Creation still flows through the direct `ModalResult` in `draw-modal.ts`;
  editing remains blocked in `ctx-menu-actions.ts`.
- [x] **`geoman-events.ts` `onEditEnd` casts `MultiLineString`/`MultiPolygon`
  coords as `[number, number][]`** — replaced with type-safe helpers:
  `positionsToLatLng` and `linearGeometryToLatLng` (MultiLineString flattens its
  nested lines; MultiPolygon takes the first polygon's outer ring). Tests cover
  both geometries.
- [x] **`LocationSearchSelect` doesn't reflect `modelValue` on edit** — an
  immediate watcher resolves the selected id back into the search box by listing
  the scoped options (`?search=&take=500`) when the box is untouched, using the
  existing `searchGen` race guard; it never clobbers text the user is typing.
  Tests cover initial value, function endpoints, and the while-typing guard.
- [x] **`SettingsUsers` location endpoints missing the caller's geographic
  anchor** (new, found during round 3) — `GET /api/communes` requires `daira_id`
  and `GET /api/dairas` requires `wilaya_id` (400 otherwise), but `daira_admin`
  and `wilaya_admin` have no selectors to supply them. The endpoints now fall back
  to `appStore.user.wilaya` / `appStore.user.daira` (populated from
  `/api/current_user`), with an explicitly chosen location taking precedence.
  Tests cover all four combinations.

## Verification (round 3)

- `npm run test:run` — 953 passed (91 files).
- Coverage: statements 74.42%, branches 64.1%, functions 77.03%, lines 75.74%
  (all above the 60/52/62/61 thresholds).
- `npm run lint`, `npm run typecheck`, `npm run lint:css`, `npm run build` — all clean.
- E2E suite (`npm run test:e2e`) — 16 Playwright specs pass locally with API
  calls mocked via `page.route`.
