# NARS API — Code Quality Issues

## ✅ Fixed (2026-07-01)

All 15 items from the initial audit have been resolved.

- [x] **🔴 `Forbid("message")` swallows error messages** — `AdminController.cs:31,33`.
- [x] **🔴 Dummy BCrypt hash will throw** — `AuthController.AdminSignup.cs:56`.
- [x] **🔴 SQL injection via table name interpolation** — Added `ValidateTableName()` allowlist.
- [x] **🟠 Enable `TreatWarningsAsErrors`** — Added to `NarsApi.csproj`.
- [x] **🟠 `JsonSerializerOptions` created 3× inline** — Extracted to `static readonly` field.
- [x] **🟠 Account-lockout message leaks valid username** — `AuthController.cs:67-71`.
- [x] **🟠 Hardcoded version string `"2.0.0"`** — Replaced with assembly version.
- [x] **🟠 Hardcoded password `"postgres"` in factory** — `AppDbContextFactory.cs:17`.
- [x] **🟠 `ConfigureNarsPipelineAsync` refactored** — Split into 7 focused methods.
- [x] **🟠 Admin update requires password for role/scope changes** — `AdminController.cs:275-286`.
- [x] **🟠 `BackgroundTaskQueue` capacity configurable via `IOptions<>`** — `BackgroundTaskOptions`.
- [x] **🟡 `using` instead of `await using` for IServiceScope** — Changed to `CreateAsyncScope()`.
- [x] **🟡 Dead null-check in `CreateEntity`** — `FeaturesController.cs:69-73`.
- [x] **🟡 Magic string index names in `OnModelCreating`** — Changed to `nameof(FeatureBase.UserId)`.
- [x] **🟡 `JwtService.ValidateToken` broad `Exception` catch** — Only `SecurityTokenException` caught.
- [x] **🟡 Validation after DB call instead of before** — `FieldController.cs:101-105`.
- [x] **🟡 `FieldService.GetFeatureOwnerAsync` double round-trip** — Single JOIN query now.
- [x] **🟡 Redundant MIME type mappings** — Removed `.js`, `.css`, `.svg`, `.woff`, `.ico` (already default).
