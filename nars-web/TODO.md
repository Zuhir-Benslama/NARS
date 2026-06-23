# Code Quality Issues

All items from the initial code quality check have been resolved.

## Fixed

- **`src/api/index.ts:163`** — `Content-Type: application/json` now only set on methods with a body (POST/PUT/PATCH), not GET/HEAD.
- **`src/map/features/loader.ts:12`** — Replaced mixed `await` + `.then()` with pure `await` pattern.
- **`src/config/index.ts:22`** — Removed unnecessary `as string` cast.
- **`src/composables/useTheme.ts:14`** — Added `parseTheme()` with validation against allowed `ThemeMode` values; corrupt localStorage falls back to `"dark"`.
- **`src/lib/validation.ts:16-39`** — Turf dynamic import failure now skips client-side check and falls through to server-side validation instead of returning early with an error.
- **`src/map/index.ts:35`** — Moved `import { destroyDrawEvents }` to top of file with other imports.
- **`src/types/store.ts:16-40`** — Removed duplicate `AppStore` interface (was identical to `AppStoreState` and unused anywhere).
- **`src/map/core/state.ts:46`** — Added explanatory comment on why the type assertion is safe.

## Not Addressed (no action needed)

- **`src/utils/sanitize.ts:10-17`** — DOMPurify SSR fallback is a documented limitation in a pure-SPA project; not currently relevant.
- **`src/map/features/feature-modal.ts:47-50`** — `window.__narsCurrentGeometry` is DEV-only and would require a larger refactor to properly pass geometry between modules.
- **`src/stores/modalStore.ts:118-120`** — Promise queue without `reject` is by design; modals always resolve on close.

## Verified

- vue-tsc: 0 errors
- ESLint: 0 errors, 0 warnings
- Stylelint: 0 errors
- Tests: 378 passed, 0 failed
