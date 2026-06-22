All items from the initial code quality check have been resolved.

## Fixed

- **vite.config.ts** — Raised coverage thresholds from `{ statements: 15, branches: 10, functions: 20, lines: 15 }` to `{ statements: 40, branches: 30, functions: 40, lines: 40 }`.
- **stylelint** — Added stylelint with `stylelint-config-standard`; created `.stylelintrc.json`; added `lint:css` and `lint:css:fix` scripts; updated lint-staged to run stylelint on CSS files.
- **lib/validation.ts** — Converted all `.then(r => r.json())` chains to consistent `await` pattern across all 5 API call functions.
- **types/features.ts, map/features/feature-data.ts** — Changed `FeatureData.type` from `string` to `FeatureTypeKey` union type (8 phase keys). Made `toApiSaveShape` switch exhaustive (removed `default: return null`). Removed null-check dead code in `undo.ts` and `feature-persistence.ts`.
- **map/map-boundary.ts** — Replaced magic number `setTimeout(..., 100)` with `requestAnimationFrame` for deferred event listener.
- **api/client.ts** — Added detailed comment explaining why `sendLogs()` intentionally bypasses `apiFetch` (fire-and-forget, avoids cascading failures, no CSRF needed).
- **utils/sanitize.ts** — Removed deprecated `sanitizeText` alias; migrated internal usage and all imports to `escapeHtml` directly.

## Verified

- vue-tsc: 0 errors
- ESLint: 0 errors, 0 warnings
- stylelint: 0 errors
- Tests: 378 passed, 0 failed
