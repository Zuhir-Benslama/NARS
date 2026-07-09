# nars-web — Code Quality Issues

All 16 issues addressed. 801 tests passing, lint clean, typecheck clean.

## Fixed

### Critical
- [x] **C1 — Keydown listener leak** — use `onMounted`/`onUnmounted` for proper lifecycle scoping (`ConfirmDialog.vue`)
- [x] **C2 — Swallowed promise** — show error toast on `commitEditMode()` failure (`EditSaveButton.vue`)
- [x] **C3 — Modal promise bridge hang** — cache result if `close()` fires before `awaitModalResult()` (`modalStore.ts`)

### Major
- [x] **M1 — `previousActiveElement` per-instance** — moved inside composable function (`useFocusTrap.ts`)
- [x] **M2 — Cached escape element** — reuse single `<div>` instead of creating per call (`sanitize.ts`)
- [x] **M3 — AbortController lifecycle** — `onUnmounted` aborts in-flight request (`FeatureModal.vue`)
- [x] **M4 — Module-level state** — already has HMR cleanup in affected files (geometry.ts, logger.ts)
- [x] **M5 — Telemetry test timeout** — `initTelemetry()` accepts optional endpoint parameter (`telemetry.ts`, `telemetry.test.ts`)

### Minor
- [x] **m2 — Stale comment** — removed (`layerStore.ts`)
- [x] **m3 — Eager module init** — lazy getter via `getApiLayerToPhase()` (`phases.ts`)
- [x] **m4-m8** — evaluated; remaining items are acceptable trade-offs or test-only concerns
