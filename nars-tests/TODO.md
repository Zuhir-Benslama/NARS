# nars/nars-tests — Code Quality Fixes

All items resolved. Build, style, and tests pass clean.

## What was done

| Issue | Fix |
|---|---|
| **nars-tests q1** `AdminControllerTests.SetUser()` duplication | Simplified to one-liner delegating to `AuthTestHelper.CreateClaimsPrincipal()`. |
| **nars-tests q2** Fully-qualified type in `JwtServiceTests.cs:59` | Removed unnecessary `NarsApi.Infrastructure.` prefix. |
| **nars-tests q3** CI missing `dotnet format` check | Added step to `backend-lint` job in `.github/workflows/ci.yml`. |
| **nars-tests q4** 4 skipped unit tests (InMemory gap) | Clear/Update/Delete already covered. Added `LocationsControllerIntegrationTests` with 5 search tests. |
| **Makefile m1** `cluster-wait` control flow fragile | Restructured `||`/`;`/`&&` chain — `until` loop only runs when nodes unreachable; single `kubectl wait` at end. |
| **Makefile m2** Missing `.PHONY` on 5 targets | Added `_build-nars-api`, `_build-nars-postgis`, `_build-nars-vite`, `db-get-pod`, `db-get-password`. |
| **Makefile m3** `.env` world-readable | Added `chmod 600 $@` after generating secrets file. |
| **Makefile m4** Secrets visible via `ps aux` in `secrets-apply` | Added security comment explaining `--from-literal` limitation. |
