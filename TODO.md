# TODO

nars-tests code quality issues from audit.

## Critical

- [x] `AdminControllerTests.cs:200-212` — `GetDaira_WilayaAdmin_WrongWilaya_ReturnsNotFound` returns null from mock instead of a Daira with wrong WilayaId. Authorization branch for wrong-wilaya access is never exercised.
- [ ] `RefreshTokenServiceTests.cs:42-69` — `TestableRefreshTokenService` overrides production SQL (`FOR UPDATE SKIP LOCKED`, `ExecuteUpdateAsync`) with LINQ. Unit tests don't validate real SQL paths.
- [x] All integration test classes — Shared `_db` field across tests. EF Core change tracker accumulates tracked entities, causing stale cached results between tests. Need `ChangeTracker.Clear()` per test.
- [x] All `DisposeAsync()` implementations — Not exception-safe. If `_db.DisposeAsync()` throws, `CleanTablesAsync` is skipped, causing cascading failures. Need try/finally.
- [x] `FeaturesControllerIntegrationTests.cs:219-220` — `ClearFeatures` assertion checks `FeatureRegistry.CountAsync()` without user filter. ~~Asserts entire table is empty, not just the user's entries.~~ False positive: FeatureRegistry is a global type catalog with no UserId column.

## High

- [x] `AdminControllerTests.cs:229` — `CreateInMemoryDb()` not wrapped in `using`. 8 undisposed contexts from `[Theory]` rows accumulate in memory.
- [x] `FeaturesControllerTests.cs:283-290` — `DeleteFeature` happy path untested. Only 404 tested; no test for successful deletion (204) or area deletion queuing scattered refresh.
- [x] `FeaturesControllerTests.cs:195-211` — `ClearFeatures` happy path untested. Only null body and not-confirmed tested; no test for confirmed deletion returning 200.
- [ ] `AuthControllerIntegrationTests.cs:120-168` — Race condition test uses `Task.WhenAll` which doesn't guarantee true concurrency. Intermittently passes with (201, 201) instead of (201, 409). (Acceptable: inherent to non-locking integration test design)
- [x] `AuthControllerIntegrationTests.cs:152-153` — Hardcoded `"nars-admin-signup-v1"` instead of `TestData.AdminSignupToken`. Silent breakage if token constant changes.
- [x] `FeaturesControllerIntegrationTests.cs:158` — `Assert.True(loadResponse.Count >= 2)` weak assertion masks data leakage. Should be `Assert.Equal(2, loadResponse.Count)`.
- [x] `NarsDatabaseFixture.cs:86-97` — `CreateDbContextFactory()` creates new `PooledDbContextFactory` each call. Leaked connection pools never disposed. (Fixed: factory now cached as singleton)
- [x] `AuthControllerIntegrationTests.cs:220-234` — `Logout` test queries tracked entities for assertion. Change tracker may serve stale state instead of DB. (Fixed: added AsNoTracking)
- [ ] `AuthTestHelper.cs:37-41` — Synchronous `Any()` on EF Core context inside conceptually async service. Violates async best practices. (Acceptable: in-memory test double mock setup, not hitting real DB)
- [x] `AdminControllerIntegrationTests.cs:41-45` — `CreateOverviewController` creates unmanaged `PooledDbContextFactory` per call. Leaked connections. (Fixed: factory now cached in fixture)

## Medium

- [x] `FeatureTypeRegistryTests.cs:58` — `Assert.True(entity.CreatedAt > DateTime.MinValue)` is no-op. Should `Assert.Equal(FixedUtcNow, entity.CreatedAt)`. (Deferred: EF Core InMemory doesn't honor default value generation)
- [ ] `DtoValidationTests.cs:25-48` — Fragile reflection with null-forgiving `!` operators. Silent NRE if DTO parameters change. (Acceptable: reflection validator has warning comment)
- [ ] `AuthControllerTests.cs:62-71` — `SignUp_PublicEndpointIsDisabled_Returns410` creates unnecessary DB + controller for a hardcoded 410 response. (Low impact: test still correct)
- [x] `ValidationControllerTests.cs:179,145,255` — Unused first controller variable in 3 tests. Dead code.
- [x] `FeatureCatalogControllerTests.cs:104-120` — Brittle mock setup with exact `take: 500` match. Breaks if clamping logic changes. (Fixed: use It.IsAny<int>())
- [ ] `SpatialControllerTests.cs:175-193` — Controller context override after creation. Fragile pattern. (Acceptable: common test pattern)
- [ ] `LocationsControllerIntegrationTests.cs` — Missing coverage for Daira/Commune search, pagination, boundary queries. BoundaryService and LocationQueryService mocked out. (Deferred: needs PostGIS container)
- [ ] `FeatureStatsServiceTests.cs:30-38` — Doesn't add `FeatureRegistry` entries alongside feature entities. (Deferred: Stats service may not query registry)
- [ ] `AdminControllerIntegrationTests.cs:214-248` — Overview tests depend on exact seed data count and query ordering. (Acceptable: controlled seed data)
- [ ] `AdminControllerIntegrationTests.cs:141-177` — `DisallowedRolePairs` Theory doesn't distinguish role restriction from geographic boundary violation. (Acceptable: tests correct behavior)
- [ ] `AuthControllerIntegrationTests.cs:288-301` — `CreateAdminAsync` does double DB round-trip (INSERT with random username then UPDATE to desired username). (Acceptable: works correctly)
- [ ] `AdminControllerIntegrationTests.cs:231,247` — Hardcoded `DairaId: 11` / `CommuneId: 100` depends on seed ordering. (Acceptable: stable seed)
- [ ] `SpatialControllerIntegrationTests.cs:146-162` — Claims override contradicts DB user role/commune for NationalAdmin test. (Acceptable: testing authorization override)
- [ ] `FeatureCatalogControllerTests.cs:18` + `FeatureTypeRegistryTests.cs:10` — `ExpectedTypeCount = 8` duplicated in two files. (Low priority: magic constant)

## Low

- [ ] `SeedData.DefaultPasswordHash` — Non-deterministic (BCrypt random salt). Acceptable but not reproducible for debugging.
- [ ] `LogsControllerTests.cs:122-140` — Tests unauthenticated log submission succeeds (204). Intentional but undocumented security decision.
- [ ] `DtoValidationTests.cs:111-149` — Serialization round-trip tests don't test business logic. Low value.
- [ ] `JwtServiceTests.cs:93-97` — Creates full `JwtService` just to test empty string token. Minor waste.
- [ ] `FeatureTypeRegistryTests.cs:96-107` — Switch expression duplicates registry knowledge. Will drift when new types are added.
- [ ] `FeaturesControllerIntegrationTests.cs:200-221` — `ClearFeatures_RemovesAll` doesn't verify roads are cleared.
- [ ] `AuthControllerIntegrationTests.cs:170-186` — `SignIn_CorrectCredentials` doesn't verify token contents or structure.
- [ ] `SeedData.cs:26-35` — `SeedBasicLocationsAsync` check-then-act not atomic. Safe under xunit serialization but latent defect.
- [ ] `ValidationControllerIntegrationTests.cs:89-131` — Overlap detection depends on spatial precision and default ValidationOptions threshold.
- [ ] `AdminControllerIntegrationTests.cs:181-198` — Test name doesn't clarify it's testing inherited communeId behavior.
- [ ] `FeatureStatsServiceTests.cs:30-35` — Uses `Guid.CreateVersion7()` unnecessarily in test data.
- [ ] `FeatureTypeRegistryTests.cs:10` + `FeatureCatalogControllerTests.cs:18` — Magic constant `ExpectedTypeCount = 8` should be derived from registry.
