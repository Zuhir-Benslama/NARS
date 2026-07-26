# TODO

## nars-tests

### High

- [x] **`AppDbContext` never disposed in unit tests** — Added `using` to all `CreateDb()` / `CreateInMemoryDb()` calls. (`RefreshTokenServiceTests.cs`, `ValidationControllerTests.cs`, `AuthControllerTests.cs`, `FeaturesControllerTests.cs`, `FieldControllerTests.cs`, `SpatialControllerTests.cs`, `AdminUserControllerTests.cs`)
- [x] **`AuthTestHelper.CreateUserCreationMock` reimplements production logic** — Replaced with real `UserCreationService(db)` in all 4 consumer files (unit + integration).
- [x] **`TestableRefreshTokenService` overrides production methods** — Accepted: integration tests (AuthControllerIntegrationTests, FeatureStatsServiceTests) cover the real PostgreSQL `FOR UPDATE SKIP LOCKED` code paths via Testcontainers.
- [x] **`DtoValidationTests` uses fragile reflection-based validator** — Retained with existing WARNING comment; the reflection pattern matches MVC model-binding behavior for records. `CanBeSerialized` tests moved to separate `DtoSerializationTests` class.

### Medium

- [x] **`#pragma warning disable CA1862` hides case-sensitivity mismatch risk** — Removed with mock deletion. The real `UserCreationService` handles case normalization via `ToLowerInvariant()`.
- [x] **Duplicated controller creation boilerplate** — Accepted: each controller has unique constructor dependencies and auth setup. `AuthTestHelper.SetUser` is used where applicable.
- [x] **Duplicated `AddRoadAsync` helper** — Consolidated to `TestData.AddRoadAsync(db, userId, coordsJson, registerInFeatureRegistry)`.
- [x] **`ExpectedTypeCount = 8` defined in two places** — Unified to `TestData.ExpectedFeatureTypeCount`.
- [x] **Unused variable `otherUserId`** — Removed from `RefreshTokenServiceTests.cs`.
- [x] **Magic numbers without constants** — Extracted to `TestData.DefaultMaxBatchSize`, `TestData.DefaultMaxEntryLength`, `TestData.OversizedDataLength`.
- [x] **Inconsistent assertion patterns for status codes** — Accepted: `Assert.IsType<ObjectResult>` + `StatusCode` check is the correct pattern for error responses; `OkObjectResult` is used for success paths.

### Low

- [ ] **Static `UserId` fields in test classes** — All tests within a class share the same user identity. Safe with isolated InMemory DBs, but fragile if isolation ever breaks. (`ValidationControllerTests.cs:20`, `FeaturesControllerTests.cs:22`, `FieldControllerTests.cs:21-22`, `RefreshTokenServiceTests.cs:16`)
- [ ] **`JwtServiceTests` passes `null` for constructor parameters** — `new JwtService(secret, null, null, ...)` for `IRefreshTokenService` and `IUserProfileService`. If the constructor ever accesses these without null checks, tests break with unclear errors. (`JwtServiceTests.cs:28`)
- [ ] **`SeedData.DefaultPasswordHash` computed at class load** — BCrypt cost paid once at test startup. Fine for correctness but adds ~100ms to first test. (`SeedData.cs:14-15`)
- [ ] **Integration test `CleanTablesAsync` truncates sequentially** — Queries `information_schema` then truncates all tables. Could add latency with many tables. (`Integration/NarsDatabaseFixture.cs:108-124`)

## Makefile

### Medium

- [x] **`builtin echo` in `db-restore`** — `builtin echo "$$PASS"` uses unnecessary bash jargon. `echo` is already a shell builtin; `builtin` is a micro-optimization that confuses readers. Replace with plain `echo`. (lines 458, 461)
- [x] **Comment inside `cluster-clean` recipe** — A comment line between recipe lines works with `.ONESHELL` but is unconventional. Moved above the target. (line 183)
- [ ] **`images-push` inconsistent formatting** — Already uses `.ONESHELL`-style; no change needed.
- [ ] **`_rnd_cmd` obscure `$$1` pattern** — Make variable contains a shell snippet with `$$1` (positional parameter to `_RND()`). Works correctly but is hard to read without context. Add inline comment. (line 35)
- [ ] **`observability-port-forward` no duplicate guard** — Running twice creates duplicate background `nohup` processes. Add port-in-use checks or use lockfile. (lines 1027-1035)
- [ ] **`db-restore` path allows `..` traversal** — Regex `[^a-zA-Z0-9._/-]` permits `..` in FILE paths. Low risk since it's a local CLI tool. (line 441)

### Low

- [ ] **Forward reference of `OBSERVABILITY_NAMESPACE`** — `cluster-status` (line 203) uses `$(OBSERVABILITY_NAMESPACE)` defined 680 lines later (line 888). Make handles lazy evaluation fine, but hurts readability for new contributors. Move variable definition closer to top.
