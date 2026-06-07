# nars-web (Vue 3 Frontend) — TODO

## P1 — CI & Quality Gates
- [x] CI pipeline exists at root `.github/workflows/ci.yml` — covers typecheck, lint, test:coverage, build for `nars-web`
- [x] Coverage thresholds raised from 10% to 15% (stats), 10% (branches), 20% (funcs), 15% (lines) — 50% TBD after adding component tests
- [x] Removed `"prepare": "husky"` from `package.json` (no `.husky/` directory existed)

## P2 — Code Cleanup
- [x] Deduplicated `syncCounts()`, `openModal()`, `openEditModal()`, `resolveModal()` — removed standalone definitions from legacy `store/index.ts`; all consumers already import from `stores/`
- [x] Extracted `v-click-outside` directive from `main.ts` into `src/directives/clickOutside.ts`
- [x] Deprecated legacy `src/store/` compatibility layer — marked with removal deadline (2026-Q3), only re-exports remain (no files import from it)
- [x] Move `selectedFeatureDbId` module-level state into Pinia — extracted to `src/stores/selectionStore.ts` (separate store via `useSelectionStore()`), removed module-level `let` from `layerStore.ts`
- [x] Generate TypeScript API client from NARS OpenAPI spec — added `openapi-typescript` (v7), created `src/api/schema.d.ts` with all endpoint types, created `src/api/client.ts` with 25 typed methods. Run `npm run codegen` to regenerate from the live backend.

## P3 — Test Coverage
- [x] Add E2E tests for critical map flows (draw, edit, save, phase navigation) — 10 Playwright tests, all API calls mocked
- [x] Add component tests for largest files: `AdminDashboard.vue` (409), `SettingsUsers.vue` (533), `EntranceInspectionForm.vue` (307)

## P3 — Observability & Docs
- [x] Verify OTel traces ship correctly in production configuration
  - Backend sends OTLP gRPC to `otel-collector.observability:4317` with `service.name=nars-api`
  - Frontend sends OTLP HTTP to `/v1/traces` proxied by nginx to collector at `:4318` with `service.name=nars-vite` (fixed — was missing `service.name`, now set via `resourceFromAttributes`)
  - Collector forwards traces to Tempo (`tempo:4317`)
  - To verify in production: Grafana → Explore → Tempo datasource → `{service.name="nars-api"}` or `{service.name="nars-vite"}`
- [x] Add contributing guidelines (`.github/CONTRIBUTING.md`)
