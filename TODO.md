# TODO

## Makefile Code Quality Issues

### High

- [x] `secrets-validate` (line 688-699) — false positive when `kubectl kustomize` fails: empty output passes `grep -q REPLACE_ME`, printing "✓ No placeholder values found" despite broken manifests. Fix: capture exit code, fail on non-zero, fail on empty output.

## nars-tests Code Quality Issues

### Medium

- [x] `Integration/NarsDatabaseFixture.cs:57-61` — `DisposeAsync()` calls `_container.StopAsync()` then `_container.DisposeAsync()` without try/finally. If `StopAsync()` throws, `DisposeAsync()` never runs and the container leaks. Also affects the `catch` block in `InitializeAsync()` (line 35) which calls `DisposeAsync()` — if the container was never started, `StopAsync()` may throw. Fix: wrap in try/finally.
- [x] `Integration/NarsDatabaseFixture.cs:61-68` — `CleanTablesAsync()` TRUNCATE list is hardcoded (`naming_panels, public_spaces, public_buildings, house_entrances, roads, city_centers, districts, areas, feature_registry, refresh_tokens, users`). Every new feature table requires a manual update. A missing entry causes silent test pollution. Fix: query `information_schema.tables` to truncate all non-system tables.
- [x] `ContractTests/OpenApiContractTests.cs` — Both tests permanently `[Skip]`ped with no plan to run them. Dead test code that still compiles and consumes build time. Fix: removed the file and directory entirely.

### Low

- [x] `RefreshTokenServiceTests.cs:31-62` — `TestableRefreshTokenService` subclass overrides `FindRefreshTokenByHashAsync` and `RevokeAllUserTokensAsync` to replace PostgreSQL-specific `FOR UPDATE SKIP LOCKED` with LINQ. Unit tests therefore exercise different code paths than production. Fix: added comment explaining why it exists and that integration tests cover the real paths.
- [x] `AdminControllerTests.cs:125-139` — `CanCreateRole_ValidatesCorrectly` creates an InMemory DB via `CreateInMemoryDb("AdminControllerRoleTest")` but `CanCreateRole` is a pure role-hierarchy check with no DB query. The DB is only needed to satisfy the `UserAuthorizationService` primary constructor (class, not interface). Fix: added explanatory comment.
- [x] `DtoValidationTests.cs:30-56` — Custom `ValidateRecord<T>` uses reflection to check `[Required]` on constructor parameters. Fragile to DTO constructor changes (parameter additions, reordering) that don't produce compiler errors. Fix: added warning comment to keep in sync with actual DTO constructors.
