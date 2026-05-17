# nars-vite (Vue 3 Frontend) — TODO

## P1 — CI & Quality Gates
- [ ] Create CI pipeline (`.github/workflows/`) — `typecheck`, `lint`, `test:coverage`, `build`
- [ ] Raise coverage thresholds from 10% to ≥50% — currently too low to prevent regression
- [ ] Commit `.husky/` directory or remove husky config from `package.json`

## P2 — Test Coverage
- [ ] Add E2E tests for critical map flows (draw, edit, save, phase navigation)
- [ ] Add component tests for largest files: `AdminDashboard.vue` (409), `SettingsUsers.vue` (533), `EntranceInspectionForm.vue` (307)
- [ ] Remove legacy `src/store/` compatibility layer or set explicit removal deadline

## P3 — Observability & Docs
- [ ] Verify OTel traces ship correctly in production configuration
- [ ] Add contributing guidelines (`.github/CONTRIBUTING.md`)
- [ ] Generate TypeScript API client from NARS OpenAPI spec (reduce manual `api/` code)
