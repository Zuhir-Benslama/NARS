# Code Quality — Issues

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Low | `TestData` class exists but not used everywhere; tests still hardcode `"Str0ng!Pass"`, phone numbers, etc. | `PasswordValidatorTests.cs`, `DtoValidationTests.cs`, `FieldControllerTests.cs` | ✅ Fixed |
| Low | Two skipped DTO tests test the exact same thing — one is redundant | `DtoValidationTests.cs` | ✅ Fixed |
| Low | `CleanTablesAsync` truncates `refresh_tokens`/`users` — destroys auth state for subsequent tests | `Integration/NarsDatabaseFixture.cs` | ✅ Fixed (added `CleanFeatureTablesAsync`) |
| Low | Naming inconsistency — 3 different patterns across test files | All test files | 🔶 Partially (renamed outliers) |
| Low | Missing edge case coverage: null/whitespace inputs, extreme lengths, degenerate geometries | Multiple files | ✅ Fixed |
| Low | `FeaturesControllerTests.CreateController` returns tuple; callers discard context with `_` | `FeaturesControllerTests.cs` | ❌ By design; low value to change |
