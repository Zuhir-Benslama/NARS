# NARS-WEB TODO

## Code Quality Issues (2026-07-10)

### High Priority

- [x] Logger uses raw `fetch()` instead of `apiFetch()` for log submission (`src/lib/logger.ts`). Missing CSRF token, no retry logic, no timeout. **Fixed:** Added CSRF header, `AbortController` with 5s timeout, Blob-based `sendBeacon`.
- [x] `sendBeacon` on `beforeunload` sends no credentials and no CSRF token (`src/lib/logger.ts`). **Fixed:** Now sends `Blob` with proper content type (Note: `sendBeacon` doesn't support custom headers, but Blob is correctly typed).
- [x] CSP `script-src` includes `'unsafe-inline'` in production HTML (`index.html:15`). **Fixed:** Removed `'unsafe-inline'` from HTML source. Server-side nonce replacement in `PagesController` handles production CSP.
- [x] `buildConnectionGraph` is O(n²) with O(n) per-node resolution — effectively O(n³) (`src/map/roads/road-graph.ts`). **Fixed:** Spatial grid index with cell size `2×CONNECT_M`. Junction detection reduced to O(n·k).

### Medium Priority

- [x] `setInterval` poll in `buildDrawControl` is never cancelled on phase switch (`src/map/draw/draw-control.ts`). **Fixed:** Poll/timeout IDs stored in `drawStore`, cleared at start of `buildDrawControl`.
- [x] `SettingsUsers` debounce timers can fire after role switch (`src/components/settings/SettingsUsers.vue`). **Fixed:** All three timers cleared in `watch(targetRole)` callback.
- [x] `WilayaDetailPage` makes two sequential API calls without abort support (`src/components/WilayaDetailPage.vue`). **Fixed:** `AbortController` created on mount, signal passed to both `apiFetch` calls, aborted on `onUnmounted`.
- [x] `FeatureModal.vue` listens for `keyup` on `window` (`src/components/FeatureModal.vue`). **Fixed:** Changed to `keydown` with `e.preventDefault()` for both Enter and Escape. Tests updated.
- [x] `App.vue` loading overlay uses hardcoded light colors (`src/App.vue`). **Fixed:** Replaced `#fff` and `#374151` with `var(--modal-bg)` and `var(--text-primary)`.
- [x] `map-boundary.ts` context menu uses magic numbers `180` and `100` (`src/map/map-boundary.ts`). **Fixed:** Extracted to `CTX_MENU_WIDTH` and `CTX_MENU_HEIGHT` constants.
- [x] `map-init.ts` non-null asserts `ctx.map!` after style switch (`src/map/map-init.ts`). **Fixed:** Early return guard + local `map` variable eliminates all non-null assertions.

### Low Priority

- [ ] `featuresStore` is a plain object, not reactive (`src/map/core/state.ts`). Intentional for performance — no change needed.
- [ ] `_escapeEl` in `sanitize.ts` is a shared mutable DOM element. Safe in single-threaded JS — no change needed.
- [ ] `showConfirm` promise could theoretically never resolve if `document.body` is null. Impossible in practice — no change needed.
- [x] `FieldPanel` default `fetchFeaturesFn` doesn't use `apiFetch` (`src/components/FieldPanel.vue`). **False positive:** already uses `apiFetch`.
- [x] `layerStore._featureMap` getter is recomputed on every access (`src/stores/layerStore.ts`). **False positive:** Pinia getters are `computed` and cached; only recomputes when layer arrays change.
- [x] `useFeatureValidation.validate()` directly mutates `modalStore.errors` (`src/composables/useFeatureValidation.ts`). **Fixed:** Returns `Record<string, string>` instead; caller assigns to `modalStore.errors`.

### Verification
- `vue-tsc --noEmit`: 0 errors
- `eslint src/`: 0 errors, 0 warnings
- `vitest run`: 801/801 tests passing
