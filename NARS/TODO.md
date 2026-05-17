# NARS (.NET Backend) — TODO

## P1 — Build & CI
- [ ] Fix `Microsoft.NET.Test.Sdk` version typo (`18.5.1` → `17.x`) — blocks NuGet restore in `NarsApi.Tests.csproj`
- [ ] Create CI pipeline (`.github/workflows/`) — build, unit tests, integration tests (Docker-optional)
- [ ] Add root `README.md` inside `NARS/` project directory

## P2 — Code Quality
- [ ] Add `[ProducesResponseType]` attributes to controllers missing them (`AuthController`, `AdminController`, `LocationsController`, etc.)
- [ ] Add `Directory.Build.props` to share `TreatWarningsAsErrors` and analyzer config across projects
- [ ] Add Swagger UI in production (not just dev-mode OpenAPI)
- [ ] Replace remaining raw ADO.NET with EF Core where practical (reduce `#pragma warning disable EF1002`)

## P3 — Security Hardening
- [ ] Tighten CORS in production profile (`AllowAnyOrigin` → specific origins)
- [ ] Tighten CSP (`https:` in img-src/connect-src → specific domains)
- [ ] Restrict `AllowedHosts: *` in production `appsettings.json`
- [ ] Pin `OpenTelemetry.Instrumentation.EntityFrameworkCore` to stable (currently `1.15.1-beta.1`)
