# NARS Tests TODO

Code quality issues found during review of the test project (build clean,
0 warnings; 412 unit + 91 PostgreSQL-backed). Grouped by severity.

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

## Round 2 (post-M2)

- [x] **Dead code** — `AuthenticationExtensionsTests.cs` private `BuildStampValidationOptions()` never called (and had a pointless `async`); removed.
- [x] **Fragile exact-double assertions** — `Service/FeaturesControllerServiceTests.cs` `Assert.Equal(36.80, lat)` / `Assert.Equal(3.00, lng)` now use a 4-decimal precision delta (`36.80` is not binary-exact; any PostGIS/serialization normalization could otherwise break the test).
- [x] **Missing `SecurityStamp` in test users** — added `SecurityStamp = User.GenerateSecurityStamp()` to every `new User { ... }` block that lacked it (30 blocks across `AdminUserControllerTests`, `AdminOverviewServiceTests`, `AuthControllerTests`, `UserAuthorizationServiceTests`, `RefreshTokenServiceTests`, `Service/FieldControllerServiceTests`). EF InMemory ignores NOT NULL so tests passed either way, but the data now matches `SeedData`/the security-stamp feature under test.
- [x] **Non-deterministic `OtherUserId`** — `FieldControllerTests.cs` `static readonly Guid OtherUserId = Guid.NewGuid()` pinned to a fixed GUID (`22222222-…`), consistent with deterministic `TestData.UserId`.
- [x] **Discard-based Base64 sanity check** — `JwtServiceTests.cs` `_ = Convert.FromBase64String(hash);` replaced with `Assert.Equal(32, Convert.FromBase64String(hash).Length)` (also asserts it is a SHA-256 digest).
- [x] Verified: build 0 warnings/0 errors; 410 unit + 91 service tests pass.

## Round 3 (fresh review)

- [x] **`BootstrappingRegistrationTests.cs`** — added `Segmentation:BaseUrl` to test config and assertions that the full service graph is registered: `IEntranceQueryService`, `IRoadQueryService`, `IUserProfileService`, `IUserCreationService`, `ICommuneScopeService`, `IDraftFeaturesService`, `IErrorLogService`, plus the `RefreshTokenPruner` hosted service. Each asserted type verified against `nars-api` DI registrations.
- [x] **`AuthenticationExtensionsTests.cs`** — cookie header now uses `CookieNames.AccessToken`; log-verification tests use `Mock<ILoggerFactory>` + a proper mock logger instead of `NullLogger` (message fragments match `AuthenticationExtensions.cs` `[Auth] ...` log calls verbatim); mislabeled "non-bearer scheme" test renamed to reflect it actually covers a claims-payload failure.
- [x] **`PagesControllerTests.cs`** — `CookieNames.AccessToken`/`RefreshToken` replace remaining hardcoded cookie names.
- [x] **`InfrastructureServicesTests.cs`** — hardcoded `"commune_user"` role replaced with `UserRoles.CommuneUser`.
- [x] **`FeatureTypeRegistryTests.cs` / `FieldControllerTests.cs`** — remaining literal keys replaced with `FeatureTypes.HouseEntranceLayers.Main`, `FeatureTypes.HouseEntrance`; `FieldControllerTests` case-normalization assertion now uses `FeatureTypes.Road.ToUpperInvariant()`.
- [x] **`AuthControllerTests.cs`** — signup `CommuneId: 2` now uses `TestData.CommuneId2` (new deterministic constant).
- [x] **Naming drift** — `RefreshTokenServiceTests.cs` (`OriginalStamp` → top, PascalCase in `CreateToken_SecurityStampChange_RevokesOldTokens`), `AdminOverviewServiceTests.cs` (PascalCase `GetDraftReviews`), `GeometryHelperTests.cs` / `PasswordValidatorTests.cs` (fix-naming alignment) — standardized on `Method_Scenario_Expectation`.
- [x] **Non-findings re-verified** — `TestableDraftFeaturesService` subclass in `DraftFeaturesTests.cs` is intentional (InMemory provider lacks `ExecuteUpdateAsync`; real path covered by Service tests). No `async void`, `Thread.Sleep`, sync-over-async, or `Assert.True(x == y)` anywhere in the suite. `xunit.runner.json` (parallel collections, 4 threads, 120s long-test threshold) is sane.
- [x] Verified: build 0 warnings/0 errors; 412 unit tests pass (`--filter "Category!=Service"`); unit-only coverage 61.28% line / 53.54% branch (above the 50 floor).

## Round 4 (fresh review)

- [x] **`Service/NarsDatabaseFixture.cs` — Testcontainers image not digest-pinned.** `postgis/postgis:17-3.5-alpine` was a mutable tag (every infra base image in the repo is digest-pinned; prod postgis is `17-3.5@sha256:efbd2cb7…`). An upstream patch to the tag could silently change CI behavior. Fix: pinned to the amd64 digest `@sha256:a7b31f03…` (verified via `docker buildx imagetools inspect`), with a comment on how to re-verify.
- [x] **`NarsApi.Tests.csproj` — stale coverage comment.** Claimed unit-only ~63.0% line / 56.0% branch and full ~77.7% / 68.2%; measured (2026-08-16) 61.45/53.6 and 77.28/67.05. Comment updated to the current figures (threshold 50 unchanged).
- [x] **`Service/NarsDatabaseFixture.cs` — doc comment drift.** Said "class-level static container … PostGIS on first connection"; it's a collection fixture (`ICollectionFixture`) with PostGIS enabled explicitly in `InitializeAsync`. Comment corrected.
- [x] **`NarsApi.Tests.csproj` — no `TreatWarningsAsErrors`.** The API project enforces it; the test project didn't. Added `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (build stays 0 warnings).
- [x] **`make/tests.mk:1` — stray copy-paste comment.** The "PostGIS backup/restore/admin + migrations" clause was duplicated from `db.mk`; line now describes only backend + roads test targets.
- [x] **Stray `nars-tests/nars-tests/` directory** — leftover from an old coverage run (contained only an ignored `TestResults/coverage.cobertura.xml`, ~1.4 MB). Removed.

## Verification (round 4)
- `dotnet build` — 0 warnings / 0 errors (now enforced via TreatWarningsAsErrors)
- Full suite — 503/503 pass (412 unit + 91 service) against the digest-pinned container image
- Coverage (2026-08-16): unit-only 61.45% line / 53.6% branch; full 77.28% / 67.05% (both above the 50 floor)
