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
- E2E suite (`npm run test:e2e`) not run locally — requires the full stack on
  `:5000`/`:5173` (Playwright specs exist under `e2e/`).
