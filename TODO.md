# TODO

## Test Suite Issues (nars-tests)

### High

- [x] Unused `CreateDb()` calls — 15 test methods in `FieldControllerTests.cs` created InMemory databases they never use
- [x] `public new` method hiding instead of `override` in `TestableRefreshTokenService` — fixed by making base `RevokeAllUserTokensAsync` virtual and using `override`

### Medium

- [x] Inconsistent `ControllerContext` setup — `AuthControllerTests.CreateController` now sets `DefaultHttpContext`; removed redundant manual setup from 2 tests
- [x] Misleading test name `SubmitLogs_CaseInsensitiveLevel` → renamed to `SubmitLogs_PreservesOriginalLevelCasing`
- [x] Hardcoded DB credentials as fallback in contract test — now throws `InvalidOperationException` if env var missing
- [ ] InMemory DbContexts never disposed in unit tests — low-impact for InMemory provider (no connection pool), skipped
- [x] Integration test DB fixture lacks cleanup on init failure — `NarsDatabaseFixture.InitializeAsync` now calls `DisposeAsync()` on failure

### Low

- [x] `appsettings.Test.json` — deleted (dead config, no references)
- [x] BCrypt hash computed per-seed call — precomputed as static `DefaultPasswordHash` in `SeedData.cs`
- [x] Magic number `Assert.Equal(8, types.Count)` — replaced with named constant `ExpectedTypeCount`
- [ ] Contract tests permanently skipped — should run in CI with proper env var — `ContractTests/OpenApiContractTests.cs:29,46`
- [ ] Inconsistent seeding strategy — `SeedBasicLocationsAsync` uses coarse guard, `SeedAdminLocationsAsync` uses per-entity checks — `SeedData.cs:23-28,59-91`

## Makefile Issues

### High

- [x] Backup validation bug — `_pre-cluster-down-backup` checked pre-gzip path (`$$FILE`) which no longer exists after `gzip -f`; fixed to check `$${FILE}.gz`
- [x] Hardcoded `nars-control-plane` — 10 occurrences replaced with `$(CLUSTER_NAME)-control-plane` so `CLUSTER_NAME` override works
- [x] Grafana password exposed in `ps aux` via `--set` — now written to temp file and passed via `--set-file`

### Medium

- [x] Wrong paths in `db-restore` docstring — said `data/nars/postgis/backups/` but backups go to `backup/`
- [x] Misleading success messages — `storage-provisioner-wait` and `postgis-pv-fix` always printed "✓ ready" even on failure; now conditional
- [x] Deprecated `cluster-port-forward` silently worked — now prints deprecation warning to stderr
- [x] No shell-metachar validation on `IMAGE_TAG` or `FILE` — added regex checks to reject injection payloads

### Low

- [x] `helm-check` missing docstring — added for `make help` consistency
- [x] `PGPASSWORD` visible in container process table — `_pg_dump_cmd` and `db-restore` now pipe password via stdin pattern instead of `env PGPASSWORD=`
