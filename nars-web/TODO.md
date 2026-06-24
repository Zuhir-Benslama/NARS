# Code Quality — All Issues Fixed

All 7 issues identified in the initial review have been resolved (TypeScript: 0 errors, ESLint: 0 warnings, tests: 378 passed).

| Severity | Issue | Fix |
|----------|-------|-----|
| Medium | `draw-state.ts` + `drawStore.ts` — `Record<string, unknown>` instead of proper Geoman types | Replaced with `GeomanMarkerPointer` from `geoman-types.ts` and `SetLngLatFn`; removed unsafe casts in `draw-marker-patch.ts` |
| Medium | `layerStore.ts:76-83` — `getFeature()` iterates `Object.values(this.$state)` including Pinia internals | Replaced with explicit iteration over `Object.keys(createInitialState())` |
| Low | `types/features.ts` — no typed feature subtypes | Added `FeatureDataByType` discriminated union + per-type interfaces (`AreaFeatureData`, `RoadFeatureData`, etc.) |
| Low | `phases.ts:100-173` — manual `API_LAYER_TO_PHASE` duplicates backend mapping | Auto-generated from `feature-types.ts` arrays via `buildApiLayerToPhase()` |
| Low | `vite.config.ts:96-101` — low coverage thresholds | Raised from 25/20/30/25 to 40/30/45/40 |
| Low | `main.ts:23-118` — monolithic IIFE | Extracted into `checkAuth()`, `createVueApp()`, `initializeApp()` named functions with clear responsibilities |
| Low | `router/index.ts` — no navigation guards | Added `beforeEach` guard with documentation (auth handled pre-Vue) |
