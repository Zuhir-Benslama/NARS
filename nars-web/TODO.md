# Code Quality Issues

## 🟠 Medium
- [ ] **Module-level mutable state with manual reset functions** — `draw/draw-state.ts`, `edit/edit-state.ts`, `snapping/snapping.ts`, `snapping/snap-sources.ts`, `undo.ts`, `map-boundary.ts` export `let` vars + `reset*()` fns. All 33 test files depend on correct teardown; missed reset = state leakage. Convert to Pinia stores or composables.
- [ ] **~50+ `as` type assertions** — weak typing masks real errors. Replace with proper type guards across codebase. (~38 removed: 20 `as LayerState`, 11 `as Error` → `getErrorMessage()`, 6 `as string`/`as number`, 1 `as string[]`)
- [ ] **Toast creates DOM elements directly** — `lib/toast.ts` uses `document.createElement` and `classList`, bypassing Vue's reactivity system. Hardcoded 3s timeout.

## 🔶 Low
- [ ] **No integration tests** — no test simulates a multi-step user workflow (e.g., draw → save → verify layer).

## ✅ Fixed
- [x] **Race condition in feature modal** — 4 bugs fixed: `prepareModalExtras` ordering, abort on empty-reset, house entrance edit dropdowns, `isEdit` guard.
- [x] **`NarsError` context typed `Record<string, unknown>`** — Removed `[key: string]: unknown` index signature, added `code?: string`.
- [x] **String-typed inspection step values** — Typed as `EntranceStep` / `NamingPanelStep` unions.
- [x] **Magic color strings** — `draw-save.ts` hardcoded hex colors replaced with `phase.color`; `naming-panels.ts` `PANEL_COLORS` derived from `PHASES`; `styles.ts` `polygonStyles` (dead code) removed; `createEntranceIconHtml` default color references `PHASES`; `ctx-menu-actions.ts` cityCenter `lineColor` hardcoded kept (unrelated to phase colors).
- [x] **Unsafe `as Error` catch-block casts** — Added `getErrorMessage()` helper to `lib/errors.ts`; replaced 11 `(err as Error).message` patterns across 7 files.

## ✅ Verified Clean (false positives)
- [x] **Tests reference `ctx.map` with incomplete mocking** — `draw.test.ts` and `snap-geometry.test.ts` don't use `ctx` at all; `undo.test.ts` properly mocks `./core/state`. None require maplibre-gl mocking.
- [x] **`ctx` proxy pattern breaks Vue 3 reactivity** — Intentional design. All consumers are non-Vue `.ts` files holding imperative map references (maplibre Map, Geoman, GeoJSON sources). No Vue reactivity depends on `ctx`.
- [x] **Barrel file circular dependency risk** — Verified: none of the 15 re-exported modules import back through the barrel. Only consumer is `main.ts`.
- [x] **Config soup** — Already well-organized with 6 cleanly separated `*_CONFIG` objects (API, MAP, SNAP, VALIDATION, UI, GEOMETRY).
- [x] **`useFeatureValidation` tightly coupled to `modalStore` shape** — Intentional by design. The `modalStore` parameter is explicitly typed as `ModalState & { phaseIndex: number | null }`, which IS the visible contract.
- [x] **`selectedFeature` type missing `dbId`** — Verified: `f.id` from `/api/field/features` IS the database UUID. The field is confusingly named `id` but is semantically `dbId`.
- [x] **I18n `currentLang` dual-path mutation** — Only one mutation path (`setLang`). No watcher on `currentLang` exists.
- [x] **Monolithic `commitSave` (300+ lines)** — `map/draw/draw-save.ts` extracted into `buildStorePayload`, `updateStoresAfterSave`, `buildStorePayload`.
- [x] **Monolithic `saveToDatabase` (250+ lines)** — already clean at 37 lines; monolithic concern was in `saveAndUpdateStore` which has been refactored.
- [x] **`name.replace(/s$/, "")`** — `components/FeatureModal.vue:178` now uses `t()` to translate the i18n key, removing fragile singularization.
- [x] **`editStore.ts` defines redundant `reset` action** — removed `resetEdit()` action; callers use `$reset()` directly.
- [x] **`localStorage` not wrapped in try/catch** — `phases-nav/storage.ts` already had try/catch; not an issue.
- [x] **Arrow function in `removeEventListener`** — not present in codebase; all listeners use named functions (false positive).
- [x] **`LayerState` uses `[key: string]`** — already properly typed with all 8 specific keys (false positive).
- [x] **Hardcoded `open` on `<details>`** — intentional UX design for admin dashboard; reasonable for typical 10-20 dairas per wilaya.
- [x] **`map/rotation.ts` has commented-out code** — no dead code present (false positive).
