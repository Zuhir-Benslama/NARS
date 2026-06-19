# nars-web (Vue 3 Frontend) — Code Quality Issues

## Completed
- Typecheck: clean (0 errors)
- Lint: clean (0 errors, 0 warnings)
- Tests: 373/373 pass (33 test files)
- Full source analysis completed

## Issues by Priority

### Critical
- [ ] **`types/store.ts:30-42`** — `AppStore` interface claims `modal: ModalState` field that doesn't exist on actual `appStore`. Any code using `AppStore` type and accessing `.modal` gets `undefined` at runtime.
- [ ] **`stores/modalStore.ts:127`** — `export let currentModalFeatureId` is mutable module-level state outside Pinia's reactivity system. Changes won't trigger Vue re-renders.
- [ ] **`stores/modalStore.ts:112-184`** — "Modal Promise Bridge" uses split state (some in Pinia, some as module-level `let`). Not SSR-compatible. `close()` drains ALL pending promises, not just the caller's.

### High
- [ ] **`api/client.ts:95`** — `getManageableUsers()` returns `Record<string, unknown>[]` — all type safety lost. Should use proper schema type.
- [ ] **`api/client.ts:100-108`** — `updateAdminUser` and `deleteAdminUser` return raw `Promise<Response>` instead of typed JSON consistent with other methods.
- [ ] **`map/features/features.ts:213-233`** — `fetchRoadSide` uses `window.__narsCurrentGeometry` which only exists in dev mode; gives wrong results in production.
- [ ] **`map/draw/draw-events.ts:34-40`** — `watch()` created outside component context with no cleanup on map destroy (watcher leak).
- [ ] **`map/features/features.ts:149`** — `saveToDatabase` catches errors but doesn't call `logError()` — server save failures silent in production.
- [ ] **`components/FieldPanel.vue:106-107`** — catch block is completely silent; user gets no feedback on network/server errors.

### Medium
- [ ] **`map/core/types.ts`** — ~142 lines of type definitions (`MapContext`, `SnapState`, `DrawingState`, `EditState`) that are duplicates of actual runtime types. Essentially dead code.
- [ ] **`components/AdminDashboard.vue:140` + `WilayaDetailPage.vue:44`** — duplicate `slugify` function. Extract to `src/utils/`.
- [ ] **`stores/layerStore.ts:76-84`** — `getFeature(dbId)` is O(n) scan of all layer arrays. Build a `Map<dbId, LayerEntry>` index.
- [ ] **`composables/useApiFetch.ts:72-83`** — `apiRequest` helper silently returns `null` for ALL errors; caller cannot distinguish "no data" from "error".
- [ ] **`lib/telemetry.ts:10`** — OTEL endpoint defaults to `/v1/traces` (local path) which won't work in production.
- [ ] **`map/map-init.ts:31-37`** — tile URLs hardcoded instead of reading from `config/index.ts`.
- [ ] **`map/context-menu/context-menu.ts:60`** — city center lock menu item has no-op `onClick`.
- [ ] **`map/core/state.ts:142-168`** — `featuresStore.updateSource()` called on EVERY mutation; `clear` + `batchAdd` on startup triggers 2 GeoJSON source updates unnecessarily.

### Low
- [ ] **`map/features/features.ts:1-257`** — single-responsibility violation: geometry, API calls, DB saves, BIS computation all in one file. Extract into focused modules.
- [ ] **`map/phases/`** — application-level concerns (navigation, localStorage) co-located in map module. Move to `src/stores` or `src/composables`.
- [ ] **Duplicate commune name fallback** — `appStore.user?.commune?.name_fr` fallback logic duplicated in `appStore.ts` (setUser), `FeatureModal.vue:224`, and `features.ts:177`. Extract to utility.
- [ ] **`main.ts:127`** — `window as unknown as { __TEST__: TestStores }` double assertion.
- [ ] **`stores/index.ts`** — re-exports standalone module-level functions alongside Pinia stores, creating a confusing mixed interface.
- [ ] **Mixed store key naming** — `defineStore("app", ...)` (singular) vs `defineStore("layers", ...)` (plural). Inconsistent.
- [ ] **`lib/errors.ts:266`** — `logError` calls `console.error` in production, leaking error details to browser console.
- [ ] **`lib/logger.ts:49`** — silent catch in logger means log delivery failures are invisible.
- [ ] **`map/snapping/snapping.ts:30-37`** — HMR cleanup uses `import.meta.hot.dispose()` without checking if `import.meta.hot` exists.
- [ ] **Dead cleanup** — `src/types/api.ts:29-33` `ScatteredRefreshResponse` type likely unused.

### Hardcoded Values (Should Be Config)
- [ ] **`map/map-init.ts:45-51`** — map center `[2.5, 28.0]` and zoom `5` hardcoded instead of using `MAP_CONFIG` defaults.
- [ ] **`components/AdminDashboard.vue:231-233`** — wilaya grid `grid-template-columns: repeat(4, 1fr)` hardcoded, not responsive.
- [ ] **`lib/toast.ts:38-42`** — toast colors hardcoded instead of CSS variables.

### Vue Component Polish
- [ ] **`App.vue:12-49`** — near-duplicate template blocks for `isFieldWorker` vs else branch. Extract shared loading/error into sub-component.
- [ ] **`FeatureModal.vue:194-232`** — 4 separate `watch()` calls on different modal properties. Consolidate into single watcher or `computed`.
- [ ] **`components/FieldPanel.vue`** — no props defined; all state from direct `apiFetch` calls. Couples tightly to API layer.
- [ ] **`FeatureModal.vue:167`** — `const m = modalStore` shadows store with short alias `m`, losing convention clarity.
- [ ] **`InfoPanel.vue:49`** — `const c = computed(...)` alias `c` is not descriptive.
