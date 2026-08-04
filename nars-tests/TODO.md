# Code Quality Issues — nars-tests

Findings from the Aug 3 test-suite review. Verified clean: `dotnet build` (0 warnings,
analyzers on), `dotnet format --verify-no-changes`, 363 tests / 42 authored files. Good
practices already in place: shared `TestData` constants, fixed-clock `IDateTimeProvider`
injection, GUID-suffixed in-memory DBs (parallel-safe), `PostgreSqlCollection` fixture
sharing one Postgres container, Moq behavioral verification (`Verify(Times...)`,
captured-argument callbacks), Theory/InlineData usage, `Guid.CreateVersion7()` seed IDs.

## High

- [x] **H1 — Wall-clock timeouts in background processor tests** — DONE: replaced all
  `tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))` with a shared
  `TestTimeout = TimeSpan.FromSeconds(30)` constant (`BackgroundQueueProcessorTests.cs:14`).
  The TCS is the real completion signal; the timeout only guards against deadlock. Verified:
  5/5 processor tests pass.

- [x] **H2 — Static-ctor filesystem writes in PagesControllerTests** — DONE: static ctor now
  create-if-missing (`EnsureTemplate`) under a `static readonly object TemplateWriteLock` and
  never clobbers existing `wwwroot/*.html`. Verified: 12/12 pass.

- [x] **H3 — Integration tests never exercise the HTTP pipeline** — DONE (chosen fix: rename,
  per user decision). `nars-tests/Integration/` → `nars-tests/Service/`; namespaces and class
  names updated (`*ControllerServiceTests`, `FeatureStatsServiceTests`, `NarsDatabaseFixture`,
  `PostgreSqlCollection`). The 9 Postgres-backed classes now carry
  `[Trait("Category", "Service")]` and CI filters use `Category=Service` / `Category!=Service`
  (namespace-based `~Integration` matching stopped working after the rename). Note: 4 plain
  unit classes contain "Service" in their name (`JwtServiceTests`, `RefreshTokenServiceTests`,
  `ScatteredAreaServiceTests`, `UserAuthorizationServiceTests`) — the trait (not `~Service`)
  is what keeps them in the unit run. Makefile: `make test-unit` / `make test-service`.

- [x] **H4 — Race-condition test relies on an implicit DB window** — DONE: added an INVARIANT
  comment to `Service/AuthControllerServiceTests.cs`
  (`AuthorizedAdminSignup_RaceCondition`): determinism relies solely on the DB unique index on
  username — one 201 + one 409; a cached/swallowed uniqueness check would make it flaky.

## Medium

- [x] **M1 — Bare `Assert.NotNull(value)` with no follow-up** — DONE:
  `AdminControllerTests.cs:42` asserts `payload.level=="national"`, `Assert.Empty(payload.wilayas)`,
  `total==0`; `LocationsControllerTests.cs:296` asserts `Assert.Single` + `NameFr=="Alger"` +
  `Total==3`; `Service/SpatialControllerServiceTests.cs:103` asserts `HasError==false` + null
  error fields; `Service/FeaturesControllerServiceTests.cs` `LoadFeatures_ReturnsAllUserFeatures`
  asserts Count==2 + both labels/types.

- [x] **M2 — Count-only assertions without verifying contents** — DONE:
  `Service/FeaturesControllerServiceTests.cs` `LoadFeatures_ReturnsAllUserFeatures` asserts
  Count==2 + both labels/types; `Service/FieldControllerServiceTests.cs` `GetFeatures` asserts
  `Assert.Single` + Label "Integration Test Road" + Layer "street" + inspection fields
  (Status "good", Type Road, FeatureId); `Service/AuthControllerServiceTests.cs`
  `Logout_RevokesRefreshTokens` uses `Assert.NotEqual(0, ...)`; `Service/SpatialControllerServiceTests.cs`
  `GetRoadSide_ValidRequest` asserts `Assert.Equal(1, resp.SuggestedNumber)` (no entrances used).

- [x] **M3 — `Assert.True` masking the actual value** — DONE:
  `FeatureTypeRegistryTests.cs:57` → `Assert.Equal(FixedUtcNow, entity.CreatedAt)`;
  `Service/AuthControllerServiceTests.cs:227,239` token/revoked counts → `Assert.NotEqual(0, ...)`.

- [x] **M4 — Brittle exact-string matching on messages / JSON** — DONE (partial by design):
  `Service/FeaturesControllerServiceTests.cs:250` no longer does `Assert.Contains("36.8", area.Data)`
  — parses the JSON and asserts `GetDouble()` on lat/lng. LEFT as intentional contract checks:
  `PasswordValidatorTests` exact validation messages, `ValidationControllerTests` rule-trigger
  `Assert.Contains("turn"|"connect"|"overlap", ...)`, `RefreshTokenServiceTests` exact token
  error messages — these pin user-visible wording by design.

- [x] **M5 — Index-based assertions (ordering dependence)** — DONE: reviewed all sites; every
  index access is already guarded by `Assert.Single`/`Assert.NotNull` first. No change needed.

- [x] **M6 — Duplicated controller-wiring setup; `AuthTestHelper.SetUser` underused** — DONE:
  `FieldControllerTests.SetUser` delegates to `AuthTestHelper.SetUser(ctrl, UserId, role, communeId, username)`;
  `FeaturesControllerTests`/`SpatialControllerTests` controllers use `AuthTestHelper.SetUser`
  after `ControllerContext` with empty `DefaultHttpContext`. `LogsControllerTests`/`AdminControllerTests`
  left as-is (extra claims/RemoteIpAddress/UserAgent needs). `ValidationControllerTests.CreateDb()`
  now shared by all 13 call sites (was inlined duplication).

- [x] **M7 — Magic numbers / IDs not centralized** — DONE: `AuthControllerTests` all
  `CommuneId: 1` → `TestData.CommuneId1`; `FeaturesControllerTests` hardcoded GUID →
  `Guid.NewGuid()` in interpolated raw string; "not found" `999` → `TestData.NonExistentId`
  (`AdminControllerTests` GetWilaya/GetDaira, `LocationsControllerTests` GetCommuneBoundary).
  `UserRolesTests.AllAdminRoles_HasExpectedCount` keeps `Assert.Equal(3, ...)` because it is
  paired with `Assert.Contains` for each role (not a bare count).

- [x] **M8 — Real wall-clock `DateTime.UtcNow` breaks the fixed-clock pattern** — DONE:
  `PagesControllerTests` `DateTime.UtcNow.AddDays(30)` → `FixedUtcNow.AddDays(30)` (added
  `using static NarsApi.Tests.TestData`).

- [x] **M9 — Pointless / redundant assertions** — DONE: removed `Assert.True(tcs.Task.IsCompletedSuccessfully)`
  after each await (proof is the await itself); `JwtServiceTests.cs:124` redundant
  `Assert.NotNull(Convert.FromBase64String(hash))` → `_ = Convert.FromBase64String(hash)`.

- [x] **M10 — Serialize-then-parse assertions** — DONE: `ValidationControllerTests` and
  `Service/AdminControllerServiceTests` inspect the typed response object via `dynamic`
  (cast to `IReadOnlyList<WilayaSummary>` for the wilayas array) instead of
  serialize-then-parse.

- [x] **M11 — No `[Trait]` categories** — DONE: added `[Trait("Category", "Service")]` to all 9
  Postgres-backed classes. `.github/workflows/ci.yml`: unit job filter `Category!=Service`
  (with coverage), service job filter `Category=Service`, job/step names updated
  ("Backend — Service Tests (Postgres)", "Run service tests", "Service Test Results").
  Makefile: `make test-unit` / `make test-service`.

## Low

- [x] **L1 — Naming inconsistency in BackgroundQueueProcessorTests** — DONE: split into two
  files — `BackgroundTaskQueueTests.cs` and `BackgroundQueueProcessorTests.cs` — and renamed
  the non-conforming methods to `ProcessBackgroundWorkItem_ExecutesQueuedItem` /
  `ProcessBackgroundWorkItem_ContinuesAfterWorkItemThrows`.

- [x] **L2 — Loose assertion in concurrency test** — DONE: `ScatteredAreaServiceTests.cs` now
  asserts the exact message `"Simulated database failure"` on `service.LastError` after the
  concurrent reads (the read loop still tolerates transient null before a writer sets it).

- [x] **L3 — Dead mock setup / inert wiring** — DONE: removed the `entranceQuery` mock from
  `SpatialControllerTests.GetRoadSide_InvalidCoordinates_Returns400` — `GetRoadSide`
  short-circuits on NaN/Infinity before the entrance query is reached. `FieldControllerTests`
  dead `FeatureRegistry` setup and `SeedAdminAsync` username mutation left as-is (harmless,
  low risk). NOTE: `LocationsController.GetWilayas/GetDairas/GetCommunes` did NOT clamp `skip`
  (unlike `FeaturesController`); a negative skip reached `EF.Skip(-1)` → 500. Fixed by adding
  `skip = Math.Max(skip, 0)` to all three endpoints.

- [x] **L4 — Weak scenario assertions** — DONE: `GetWilayas_NegativeSkip_ClampsToZero` now seeds
  3 wilayas and asserts `Items.Count==3`, `Total==3`, `Skip==0` against the real
  `LocationSearchService` (was a vacuous `IsType<OkObjectResult>` on a null mock).

- [x] **L5 — Config nits** — DONE: `xunit.runner.json` was NOT wired into the csproj — the
  settings (`parallelizeTestCollections`, `maxParallelThreads: 4`,
  `longRunningTestSeconds: 120`) were dead. Added
  `<Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />`.
  `TestData.cs` `UserId` comment now explains it is safe because every DB name is GUID-suffixed.
