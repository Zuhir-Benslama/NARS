# Code Quality TODO

## Fixed (2026-06-18)

### 1. (was) NuGet version conflict (MSB3277) — ✅ Fixed
- **File**: `nars-tests/NarsApi.Tests.csproj`
- **Fix**: Upgraded `Microsoft.EntityFrameworkCore.InMemory` and `Microsoft.EntityFrameworkCore.Relational` from 10.0.8 → 10.0.9 to match the API project.
- **Result**: `dotnet build` now 0 warnings, 0 errors.

### 2. (was) Test files excluded from linting — ✅ Fixed
- **File**: `nars-web/eslint.config.js`
- **Fix**: Removed `**/*.test.ts` and `**/*.spec.ts` from the ignore list. Added a per-file override to allow `@typescript-eslint/no-explicit-any` in test files (since mocks commonly require `any`).
- **Result**: Lint now covers test files, with only `@typescript-eslint/no-unused-vars` enforced on non-test code. Also fixed an unused variable in `e2e/map-flows.spec.ts`.

### 3. (was) `router-link` unresolved in AdminDashboard tests — ✅ Fixed
- **File**: `nars-web/src/test/setup.ts`, `nars-web/src/components/AdminDashboard.test.ts`
- **Fix**: Registered `RouterLinkStub` from `@vue/test-utils` globally (renders slot content properly). Updated the slug path test to query `RouterLinkStub` components by name instead of checking raw HTML.
- **Result**: All 23 AdminDashboard tests pass, no Vue warnings.

### 4. (was) Skipped C# tests — ⏳ Wontfix (intentional)
- **File**: `nars-tests/FeaturesControllerTests.cs`
- **Status**: 4 tests remain skipped with `[Fact(Skip = "InMemory provider does not support ExecuteDeleteAsync")]`. These are integration-level tests that require a real PostgreSQL database. They use TestContainers infrastructure but haven't been migrated yet.
