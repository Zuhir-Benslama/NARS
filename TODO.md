# TODO

## nars-tests

### High

- [ ] **`BackgroundQueueProcessorTests` — 4 tests with no assertions** (`ProcessesQueuedWorkItem`, `ContinuesAfterWorkItemThrows`, `StopAsync_CompletesGracefully`, `DisposeAsync_DisposesTokenSource`) rely on `TaskCompletionSource.WaitAsync` timeout or absence of exception as implicit pass. Add explicit `Assert` calls or replace with observable side effects. (`BackgroundQueueProcessorTests.cs:114,132,154,165`)

### Medium

- [ ] **Undisposed `AppDbContext` in controller tests** — `CreateController()` returns a context that ~30 callers across `FeaturesControllerTests`, `ValidationControllerTests`, and `LocationsControllerTests` discard with `_` instead of `using`/`Dispose`. InMemory EF Core has minimal resources, but the pattern is inconsistent with `RefreshTokenServiceTests` which disposes correctly.
- [ ] **`ScatteredAreaServiceTests.RefreshAsync_DbFailure_SetsLastError` fragile error path** — Relies on InMemory EF Core lacking raw SQL support to trigger error handling. If InMemory ever gains SQL support, the test silently passes without exercising the error path. (`ScatteredAreaServiceTests.cs:27`)
- [ ] **`RefreshTokenServiceTests` production-vs-test code path divergence** — `TestableRefreshTokenService` replaces PostgreSQL `FOR UPDATE SKIP LOCKED` with standard LINQ. Unit tests exercise different query paths than production. (`RefreshTokenServiceTests.cs:42-69`)

### Low

- [ ] **`LogsControllerTests.SubmitLogs_MessageTooLong_SkipsEntry` misleading name** — Expects HTTP 400 (batch rejected), name implies skip/204 behavior. Rename or fix expectation. (`LogsControllerTests.cs:183`)
- [ ] **`static readonly Guid UserId` in `FeaturesControllerTests` and `FieldControllerTests`** — Harmless (xUnit creates new instance per test) but inconsistent with the `using var db = CreateDb()` pattern used elsewhere. (`FeaturesControllerTests.cs:22`, `FieldControllerTests.cs:21`)
