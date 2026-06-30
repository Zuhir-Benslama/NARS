# Code Quality — Issues

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Low | Hardcoded strings not using `TestData` constants (`"Str0ng!Pass"`, `"0555..."` etc.) | 5 test files, 41 occurrences | ✅ Fixed |
| Low | Two skipped DTO tests test the exact same thing — one is redundant | `DtoValidationTests.cs` | ✅ Fixed |
| Low | `CleanTablesAsync` truncates `refresh_tokens`/`users` — destroys auth state for subsequent tests | `Integration/NarsDatabaseFixture.cs` | ✅ Fixed (added `CleanFeatureTablesAsync`) |
| Low | Naming inconsistency — 3 different patterns across test files | All test files | 🔶 Partially fixed |
| Low | Missing edge case coverage: null/whitespace inputs, extreme lengths, degenerate geometries | Multiple files | ✅ Fixed |
| Low | No `.editorconfig` — test project formatting rules may drift from API project | `nars-tests/` | ✅ Fixed |
| Low | Seed data duplication — location seeding logic duplicated across 5 test classes | Multiple files | ✅ Fixed (extracted to `SeedData.cs`) |
| Low | Tuple return from `CreateController` — callers discard context with `_` | `FeaturesControllerTests.cs` | ❌ By design; low value to change |

## Notes
- ✅ **AdminController unit tests written** — 22 tests in `AdminControllerTests.cs` covering all endpoints and `CanCreateRole` (theory with 8 cases)
- ➡️ Service-layer tests (`ValidationService`, `FeatureRepository`, `BoundaryService`) — **not written**. All three depend on raw SQL/PostGIS that `InMemory` can't fake. Integration tests already cover real paths. Low value to mock ADO.NET internals.
- ➡️ JSON parsing inconsistency — root cause is `ValidationController.MainUrbanExists` returns `new { exists }` (anonymous type) instead of a named DTO. Fixing requires a new `MainUrbanExistsResponse` record in the API project, which is an API-surface change, not a test-only change.
