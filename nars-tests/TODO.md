# NARS-TESTS TODO

## Code Quality Issues (2026-07-10)

### High Priority

- [x] **`FeatureStatsServiceTests` uses InMemory provider but service requires PostgreSQL ADO.NET** (`FeatureStatsServiceTests.cs`). Fixed: converted to integration tests using the Testcontainers PostgreSQL fixture.
- [x] **`AdminUserControllerTests` NullReferenceException in `CreateAdmin`** (`AdminUserControllerTests.cs`). `Mock.Of<IUserCreationService>()` returned null user. Fixed: DB-aware mock via `AuthTestHelper.CreateUserCreationMock(db)`.
- [x] **`AuthControllerTests` AuthorizedAdminSignup failures** (`AuthControllerTests.cs`). Same mock issue. Fixed: shared `AuthTestHelper.CreateUserCreationMock(db)` with uniqueness + password validation.
- [x] **Regression: `SignInRequest` DTO missing `[property:]` on `[Required]`** (`AuthDtos.cs:62-65`). Fixed: changed to `[property: Required(AllowEmptyStrings = false)]` so `TryValidateObject` works with records.

### Medium Priority

- [x] **`CreateDbContextAsync` is `async` without `await`** (`TestData.cs:61`). Fixed: returns `Task.FromResult<AppDbContext>(new(options))`.
- [x] **`GetScatteredStatus_ReturnsStatus` is `async` without `await`** (`Integration/SpatialControllerIntegrationTests.cs:118`). Fixed: made synchronous `void` test.
- [x] **`CleanFeatureTablesAsync` is dead code** (`Integration/NarsDatabaseFixture.cs:105`). Fixed: removed.
- [x] **Misleading test name `GetWilayas_NoSearchSkip0Take500_Caches`** (`LocationsControllerTests.cs:104`). Fixed: renamed to `GetWilayas_NoSearchSkip0Take500_QueriesDbDirectly`.

### Low Priority

- [x] **Long single-line constructor calls** (`AdminControllerIntegrationTests.cs:49`, `FeaturesControllerIntegrationTests.cs:51`, `ValidationControllerIntegrationTests.cs:48`). Fixed: multiline formatting.
- [x] **Synchronous `SaveChanges()` inconsistency** (`LocationsControllerTests.cs`). Fixed: `SeedWilayas`, `SeedDairas`, `SeedCommunes` now use `async Task` + `await SaveChangesAsync()`.
- [x] **Extra blank line** (`AuthControllerTests.cs:242`). Fixed: removed.
