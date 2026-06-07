# NARS (.NET Backend) — TODO

## P1 — Build & CI
- [x] Fix `Microsoft.NET.Test.Sdk` version — updated from `18.5.1` to `18.6.0` (current stable)
- [x] Create CI pipeline (`.github/workflows/`) — build, unit tests, integration tests (Docker-optional)
- [x] Add root `README.md` inside `nars-api/` project directory

## P2 — Code Quality
- [x] Add `[ProducesResponseType]` attributes to controllers missing them
- [x] Add `Directory.Build.props` to share `TreatWarningsAsErrors` and analyzer config across projects
- [x] Add Swagger/Scalar UI in production (not just dev-mode OpenAPI)
- [x] Replace remaining raw ADO.NET with EF Core where practical (reduce `#pragma warning disable EF1002`)
- [x] Fix N+1 in `AdminController.BuildWilayaReportAsync`
- [x] Drive `FeatureTypeRegistry.AddToDbContext` from the dictionary registry
- [x] Add integration/DB tests — Testcontainers.PostgreSql

## P3 — Security Hardening
- [x] Tighten CORS in production profile (`AllowAnyOrigin` → specific origins)
- [x] Tighten CSP (`https:` in img-src/connect-src → specific domains)
- [x] Restrict `AllowedHosts: *` in production `appsettings.json`
- [ ] Note: `OpenTelemetry.Instrumentation.EntityFrameworkCore` remains `1.15.1-beta.1` — no stable release exists (depends on experimental OTel semantic conventions)
