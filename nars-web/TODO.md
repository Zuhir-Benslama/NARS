# Code Quality Improvements — Complete

## Fixed

- **`src/map/house-numbering.ts:97`** — Replaced `console.error()` with `debugError()` for consistency with the rest of the codebase.

- **`src/map/features/feature-data.ts:83-107`** — Added explicit `ApiSaveShape` return type to `toApiSaveShape` function.

- **`src/stores/index.ts`** — Added missing exports: `useDrawStore`, `useEditStore`, `useSnapStore`, `useContextMenuStore`.

## Skipped (intentional)

- **`src/lib/telemetry.ts:15`** — `console.warn` is intentional for production (`debugWarn` only fires in DEV). Left as-is.

- **`src/map/features/loader.ts` + `src/map/rendering/geometry.ts`** — Catch blocks already use `debugError`. Adding `showToast()` would create noisy UI for background operations (commune nav, boundary rendering, scatter refresh). Left as-is.

## Verified

- TypeScript: 0 errors
- ESLint: 0 errors, 0 warnings
- Tests: 378 passed, 0 failed
