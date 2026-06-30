# nars-web Code Quality TODO

All items from the initial audit are resolved. Current status: **✅ Clean** — lint, typecheck, and 378 tests pass with zero warnings.

## Summary of fixes

| # | Severity | What | Fix |
|---|----------|------|-----|
| 1 | 🔴 | `as unknown as` casts | Removed `as unknown as GeoJSON.Polygon` in `draw-handlers.ts:93`; centralized all other Geoman internal casts into `asGeomanInternal()` helper in `geoman-types.ts` |
| 2 | 🔴 | `loader-build.ts:22` type mismatch | Removed `data as unknown as ModalResult` — direct assignment works |
| 3 | 🔴 | Dynamic key state mutation | Replaced `(state as unknown as Record<string, LayerEntry[]>)` in `geoman-events.ts` and `ctx-menu-actions.ts` with `layerStore.removeFeature()` |
| 4 | 🟡 | Prettier not in lint | Added `prettier --check` to `npm run lint` |
| 5 | 🟡 | Barrel re-export | Removed `stores/index.ts` barrel; imports point to `stores/modalStore` directly |
| 6 | ⚪ | `draw-complete.ts` Geoman casts | Moved `GeomanInternal`/`GeomanInternalActionInstance` to `geoman-types.ts` as proper typed interfaces (`GeomanLineDrawer`, `GeomanDrawFeatureData`, `GeomanActionInstance`); replaced all `as Function` with typed method calls; centralized `asGeomanInternal()` helper |
| 7 | ⚪ | Redundant `as LayerState` cast | Removed from `navigation.ts:22` |
