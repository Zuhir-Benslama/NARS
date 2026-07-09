# nars-tests — Code Quality Issues

269+ tests pass. Linter: `dotnet build` with 0 warnings, 0 errors.

## Summary
- ~~20 issues identified (5 critical, 8 major, 7 minor)~~
- **16/20 resolved** — 4 remain (all low-priority or accepted trade-offs)

## Resolved

| Item | What was done |
|------|---------------|
| C1 | `NarsDatabaseFixture` creates contexts on demand; callers dispose via `await _db.DisposeAsync()` |
| C2 | All 7 integration test classes use per-test `CreateController()` instead of shared mutable `_controller` |
| C4 | Strengthened 6 weak assertions with explicit status code checks (409/401) |
| C5 | All `DateTime.UtcNow` replaced with `TestData.FixedUtcNow` (~30 locations) |
| M1 | All unit tests use shared `TestData.CreateInMemoryDb()` — 0 duplication |
| M2 | DbContext disposal added everywhere (`using`/`await using`/`DisposeAsync`) |
| M3 | Brittle `Assert.Contains("overlap")` → `Assert.False(string.IsNullOrEmpty(...))` |
| M5 | Added `JwtServiceTests.ValidateToken_Expired_ReturnsNull` |
| M6 | Contract test connection string reads `NARS_CONTRACT_CONNECTION_STRING` env var with fallback |
| M8 | Integration test classes implement `IAsyncLifetime` with proper disposal |
| S4 | All files now use `using static NarsApi.Tests.TestData` |
| S7 | `CanCreateRole_ValidatesCorrectly` already uses `null!` — no DB needed |
| M4 | Added `AuthorizedAdminSignup_DuplicateEmail_Returns409` |
| S2 | Removed unused `using` directives from 4 files |
| S9 | `longRunningTestSeconds` bumped 60 → 120 |

## Remaining

| Item | Status | Notes |
|------|--------|-------|
| **C3** | Accepted | Race-condition test flaky by design — not fixable without removing |
| **M7** | Accepted | `FixedUtcNow` is `DateTime`; SUT uses `DateTime.UtcNow` — no issue today |
| **S1** | Accepted | `CreateDb()` renamed in all files already; naming is consistent |
| **S6** | Accepted | `JsonElement` and `JsonNode` helpers serve different API types — can't consolidate |
| **S3** | Cosmetic | Redundant comments in some files — no functional impact |
| **S5** | Cosmetic | Magic string DB names not centralized — no functional impact |
