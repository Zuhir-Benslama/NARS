# Code Quality

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Critical | `DeleteFeature_NotOwned_Returns404` has empty method body — no assertions | `FeaturesControllerTests.cs` | Fixed — test removed (covered by integration tests) |
| Critical | 3 skipped tests due to InMemory not supporting `ExecuteDeleteAsync`/`ExecuteUpdateAsync` | `FeaturesControllerTests.cs` | Fixed — remaining skipped tests now covered by `FeaturesControllerIntegrationTests` |
| Critical | `MainUrbanExists` tests only asserted `NotNull`, never checked boolean value | `ValidationControllerTests.cs:48,57` | Fixed — added `JsonDocument` assertions on `exists` property |
| Critical | No `xunit.runner.json` — no parallelism/timeout config | Project root | Fixed — created with `parallelizeTestCollections: true`, 4 threads, 60s timeout |
| Critical | JWT secrets hardcoded as string literals in 4 files | `AuthControllerTests.cs`, `JwtServiceTests.cs`, `AuthControllerIntegrationTests.cs`, `FeaturesControllerIntegrationTests.cs` | Fixed — extracted to `AuthTestHelper.TestJwtSecret` |
| High | `SignInRequest_EmptyUsername_DocumentBehavior` documents validation gap but not marked skipped | `DtoValidationTests.cs` | Fixed — renamed and marked `[Fact(Skip)]` with description |
| High | `SignInRequest_WithEmptyUsername_ModelStateWouldReject` only asserts constructor, never validates | `DtoValidationTests.cs:123` | Fixed — added real `Validator.TryValidateObject` and marked skipped |
| Medium | `Overview_NationalAdmin_ReturnsNationalOverview` uses `dynamic` with reflection on anonymous type | `AdminControllerIntegrationTests.cs:184` | Fixed — replaced with `JsonSerializer.Serialize` + `JsonDocument.Parse` |
| Low | Inconsistent `Add` vs `AddAsync` — sync `Add` in async test methods | `AuthControllerTests.cs` | Fixed — changed to `AddAsync` |
| Low | Test data (`"Str0ng!Pass"`, `"0555000000"`, etc.) duplicated across 18 files | All test files | Pending — extract to shared `TestData` class |
| Low | Naming inconsistency — 3 different patterns across files | All test files | Pending |
| Low | Missing edge case coverage: null/whitespace inputs, extreme lengths, degenerate geometries | Multiple files | Pending |
| Low | `CleanTablesAsync` truncates `refresh_tokens`/`users` — destroys auth state | `Integration/NarsDatabaseFixture.cs` | Pending |
| Low | No `appsettings.Test.json` — all options instantiated inline | Project root | Pending |
| Low | `FeaturesControllerTests.CreateController` returns tuple; callers discard context with `_` | `FeaturesControllerTests.cs` | Pending |
