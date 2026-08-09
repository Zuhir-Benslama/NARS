# NARS Tests TODO

Code quality issues found during review of the test project (build clean,
0 warnings; 488 tests: 399 unit + 89 PostgreSQL-backed). Grouped by severity.

## High

- [x] **~25 near-identical controller factories + duplicated auth wiring**
  - Service tests re-implemented the same `DefaultHttpContext { User = ... }` + `ControllerContext` block inline instead of using `AuthTestHelper.SetUser` — including two byte-identical `SetAuthenticatedUser` overloads at `Service/AdminControllerServiceTests.cs`.
  - **Fix:** added `AuthTestHelper.SetUser(T, User)` overload (`AuthTestHelper.cs`); replaced the twin `SetAuthenticatedUser` overloads and all inline blocks in `Service/AdminControllerServiceTests.cs`, `Service/FieldControllerServiceTests.cs`, `Service/FeaturesControllerServiceTests.cs`, `Service/ValidationControllerServiceTests.cs`, `Service/SpatialControllerServiceTests.cs`. Per-controller factories kept (each controller has different dependencies).
  - ✅ Verified: 62 Service tests pass.

- [x] **~60 hardcoded magic strings instead of constants**
  - Replaced feature type/layer keys (`"road"`, `"area"`, `"street"`, `"central_urban"`, `"secondary_urban"`, `"district"`, `"housing_estate"`) with `FeatureTypes.*` across `FeatureTypeRegistryTests`, `FeaturesControllerServiceTests`, `FieldControllerServiceTests`, `FieldControllerTests`, `FeatureStatsServiceTests`, `ValidationControllerServiceTests`.
  - Routes (`/login`, `/api/features`, `/api/logs`, `/api/auth/signin`) centralized as `TestData.LoginPath`/`ApiFeaturesPath`/`ApiLogsPath`/`ApiAuthSignInPath`; cookie names now use `CookieNames.AccessToken`/`RefreshToken` from the API.
  - Admin seed IDs centralized in `TestData` (`WilayaId1/2`, `DairaId10/11`, `CommuneId100/101`, `NonExistentId`) and applied in `Service/AdminControllerServiceTests`.
  - **Note:** user *labels* like `Label = "road"` in `FeaturesControllerTests.cs` were intentionally left as literal test data — they are not type keys.
  - ✅ Verified: 165 tests pass.

- [x] **Brittle hardcoded build hashes in `CacheControlTests.cs`**
  - Replaced real Vite hashes with a stable synthetic `assets/index-<hash>.<ext>` pattern.
  - ✅ Verified: tests pass.

## Medium

- [x] **`dynamic payload` shape assertions**
  - Introduced typed response DTOs in the API so the contracts are explicit and documented: `NationalOverviewResponse`, `CommuneBoundaryResponse`, `MainUrbanExistsResponse` (wire names unchanged). Updated `AdminController`, `LocationsController`, `ValidationController` and all four test sites to use them.
  - ✅ Verified: 56 tests pass.

- [x] **`DraftFeaturesTests.cs` contains two unrelated classes**
  - Split into `CommuneScopeServiceTests.cs` (13 tests, renamed to `Method_Scenario_Expectation`) + `DraftFeaturesTests.cs` (10 tests, only `DraftFeaturesServiceTests`). Added `AiDraftFeature.TypeRoad/TypeBuilding/StatusPending/StatusAccepted/StatusRejected` constants; reused `TestData` location constants.
  - ✅ Verified: 42 tests pass.

- [x] **`ChangeTracker.Clear()` workarounds**
  - Removed all three calls (`Service/AdminControllerServiceTests.cs`, `Service/FeaturesControllerServiceTests.cs`, `RefreshTokenServiceTests.cs`) — the fresh per-test `_db` already isolates state; verified the 68 affected Service tests pass without them.

- [x] **Order-sensitive async assertions — `BackgroundQueueProcessorTests.cs`**
  - `CreateProcessor(gracePeriodSeconds: 1)` → `30` so the grace-period CTS cannot expire and flake the `Assert.False(stopTask.IsCompleted)`.
  - ✅ Verified: tests pass.

## Low

- [x] Weak assertions — strengthened `UsersControllerTests.cs` (208/269/307 with `resp.User` fields), `SpatialControllerTests.cs:206` (`Message` + `GeoJson`), `RefreshTokenServiceTests.cs:397` (`Username` + `NewAccessToken`). The other flagged sites already asserted payload/DB fields. Count-only mock verification in `LogsControllerTests.cs` now also checks entry `Message`/`Level`.
- [x] `SeedData.CreateUserAsync` re-wrapping — `Service/FieldControllerServiceTests.cs` hand-built `User` with inline `BCrypt.Net.BCrypt.HashPassword(DefaultPassword)` replaced with `SeedData.CreateUserAsync`; the other thin name-generating wrappers retained (each provides a local convenience; not harmful).
- [x] `Service/AdminControllerServiceTests.cs` `SeedReferenceDataAsync` thin re-wrap — removed; calls `SeedData.SeedAdminLocationsAsync(_db)` directly.
- [x] `ScatteredAreaServiceTests.cs:115` — `Assert.All(writers, w => Assert.False(w.Result))` replaced with results from `await Task.WhenAll(writers)`.
- [x] `TestData.UserId` is a `static readonly Guid.NewGuid()` — now a fixed deterministic GUID (`11111111-…`).
- [x] `ProgramStartupValidationTests.cs` `UnreachableDatabase_FailsStartup` performed a real TCP connect to `127.0.0.1:1` — fast-failed but not hermetic. Now binds a `TcpListener` on an OS-assigned free port (never accepting) so the probe deterministically times out on a port we control; no reliance on port 1 being refused/blackholed.
- [x] Plaintext test credentials are fine — verified confined to test code (`AuthTestHelper.TestJwtSecret`, `InfrastructureServicesTests.cs` `NARS_DB_PASSWORD="s3cret"`); no shared config exposure.
