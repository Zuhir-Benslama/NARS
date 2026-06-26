# Code Quality — Issues to Address

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| ~~Medium~~ | ~~`map/features/features.ts` — likely dead code~~ Barrel file is actively imported, not dead | `src/map/features/features.ts` | Not an issue |
| Medium | PDF export uses `@vite-ignore` for unlisted deps (`html2canvas`, `jspdf`) — not in `package.json` | `src/map/export.ts` | Done — added as `optionalDependencies` |
| Medium | `layerStore.getFeature()` iterates all arrays linearly — O(n) instead of O(1) `Map<dbId, LayerEntry>` lookup | `src/stores/layerStore.ts` | Done — added `_featureMap` getter |
| Low | Type assertion `(appStore.user as { commune?: { id?: number \| string } })` — signals type mismatch in `UserInfo` | `src/phases-nav/navigation.ts:74` | Done — replaced with `appStore.user?.commune?.id` |
| Low | No `@/` path alias configured — imports use deep relative paths | `vite.config.ts` / `tsconfig.json` | Done — added to both files |
| Low | CSP includes `'unsafe-inline'` for scripts in production fallback | `index.html` | Assessed — `style-src 'unsafe-inline'` required by MapLibre GL JS; `script-src 'unsafe-inline'` only needed for Vite dev HMR. Acceptable to keep |
| Low | `map/core/state.ts` rebuilds entire GeoJSON FeatureCollection on every individual add/remove | `src/map/core/state.ts` | Assessed — batch loading already optimized; individual mutations at project scale (<1000 features) performant enough |
| Low | `sanitizeApiText` double-processes: `escapeHtml` then `DOMPurify.sanitize` — may double-encode | `src/utils/sanitize.ts` | Done — removed `escapeHtml` call; test updated |
| Low | Coverage thresholds low (29/25/34/29) | `vite.config.ts` | Done — raised to 40/30/45/40 |
| Low | Mix of Options API (`appStore`) and simpler Pinia syntax (`fieldStore`) — minor consistency | `src/stores/` | Assessed — all 9 stores use Options API; minor state typing style differences. Not urgent to standardize |
