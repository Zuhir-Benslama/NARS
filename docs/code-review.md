# NARS API — Code Review Log

> This document tracks identified issues and their resolution status.
> Security vulnerability reports should be submitted per [SECURITY.md](../SECURITY.md).

---

## Round 1 (resolved in this PR)

### 🔴 Bugs / Security Issues

#### 1. Manual JWT re-validation instead of `[Authorize]`
**File:** `NARS/Controllers/AuthController.cs:107` — `CurrentUser()`

The endpoint called `GetPrincipalFromCookie()` manually even though the JWT bearer middleware was already configured to read the same cookie via `OnMessageReceived`. Two independent validation code paths that could silently diverge.

**Fix:** `[Authorize]` attribute + `User.FindFirst(...)`. ✅

---

#### 2. Dual-validation anti-pattern in `RequireAuth()`
**Files:** `NARS/Controllers/FeaturesController.cs:293`, `NARS/Controllers/ValidationController.cs:467`

Both controllers duplicated the manual cookie→token→principal dance, bypassing auth failure events.

**Fix:** `NarsControllerBase` with `[Authorize]` + `CurrentUserId` / `CurrentCommuneId` properties. ✅

---

#### 3. Null-forgiving operators throwing `NullReferenceException`
**File:** `NARS/Controllers/ValidationController.cs:395` — `RefreshScattered()`

`Request.Cookies["access_token"]!` and `jwt.ValidateToken(token)!` would throw NRE if cookie absent, producing a misleading 500 instead of 401.

**Fix:** Replaced with `CurrentCommuneId` from `[Authorize]` pipeline. ✅

---

#### 4. Diverged copy of `PolygonFromDataSql` missing `ST_MakeValid`
**File:** `NARS/Controllers/FeaturesController.cs:327` — `TriggerScatteredRefreshAsync()`

Inline SQL omitted `ST_MakeValid`; background refresh would silently fail on invalid geometries.

**Fix:** `SqlFragments.PolygonFromData` shared constant (includes `ST_MakeValid`). ✅

---

#### 5. `Secure` flag missing on auth cookie
**File:** `NARS/Controllers/AuthController.cs:58` — `SignIn()`

Missing `Secure = true` allowed the `access_token` cookie to be sent over plain HTTP in production.

**Fix:** `Secure = !env.IsDevelopment()`. ✅

---

#### 6. `EnsureCreatedAsync()` unsafe for production
**File:** `NARS/Program.cs:117`

`EnsureCreatedAsync` silently does nothing when tables exist but schema has drifted.

**Fix:** Check `GetPendingMigrationsAsync()`; use `MigrateAsync()` when migrations exist, fall back to `EnsureCreatedAsync()` otherwise. ✅

---

### 🟡 Design / Maintainability Issues

#### 7. N+1 query chain for commune → daira → wilaya
**File:** `NARS/Controllers/AuthController.cs:68`

Three sequential `FirstOrDefaultAsync` calls = three DB round-trips.

**Fix:** Single LINQ `join` query via `LoadLocationChainAsync` / `LoadCommuneWithDairaAsync`. ✅

---

#### 8. All house entrances loaded into memory then filtered in C#
**File:** `NARS/Controllers/ValidationController.cs:353` — `GetRoadSide()`

O(n) memory load; `roadDbId` filtering happened in C#.

**Fix:** JSONB-operator SQL query filters in PostgreSQL. ✅

---

#### 9. `RequireAuth()` duplicated verbatim across controllers
**Files:** `FeaturesController.cs`, `ValidationController.cs`

**Fix:** Extracted to `NarsControllerBase`. ✅

---

#### 10. DB connection opened without checking current state
**File:** `NARS/Controllers/FeaturesController.cs:341`

Unconditional `conn.OpenAsync()` throws `InvalidOperationException` on a pooled open connection.

**Fix:** `if (conn.State != ConnectionState.Open) await conn.OpenAsync()` guards added throughout. ✅

---

### 🟢 Minor / Style

#### 11. Unused `wilaya` query in `SignIn()`
**File:** `NARS/Controllers/AuthController.cs:70`

`wilaya` was loaded but never included in the `/api/signin` response.

**Fix:** Removed; `LoadCommuneWithDairaAsync` loads only commune + daira. ✅

---

#### 12. Hardcoded localhost URL in startup log
**File:** `NARS/Program.cs:163`

Misleading in Docker/Kubernetes environments.

**Fix:** Logs `app.Urls` at `ApplicationStarted`. ✅

---

#### 13. Magic number tolerance buffer in `DistrictsCoverage()`
**File:** `NARS/Controllers/ValidationController.cs:279`

`ST_Buffer(…, 10)` was a magic number.

**Fix:** `const double DistrictBoundaryToleranceMeters = 10.0`. ✅

---

## Round 2 (post-PR review findings)

#### R2-1. `NarsControllerBase` claim properties throw `InvalidOperationException` instead of 401
**File:** `NARS/Controllers/NarsControllerBase.cs:19`

`[Authorize]` validates signature/expiry but not that application-specific claims exist. A validly signed token lacking `user_id` would surface as an unhandled 500 (or 400 via the global handler), not a correct 401. A missing claim is an authorization — not input — error.

**Fix:** Replaced throwing properties with safe `int.TryParse` expressions returning `-1` for absent claims; added `TryGetCurrentUserId` / `TryGetCurrentCommuneId` helper methods so callers can return an explicit `Unauthorized()`. ✅

---

#### R2-2. Null-forgiving `!` on `FindFirst()` in `AuthController.CurrentUser()`
**File:** `NARS/Controllers/AuthController.cs:117`

`User.FindFirst("user_id")!.Value` throws `NullReferenceException` for a validly signed JWT that lacks the `user_id` claim, producing a 500 instead of a structured error.

**Fix:** Replaced with `User.FindFirstValue()` + `int.TryParse`; returns `Unauthorized` on failure. ✅

---

#### R2-3. `LineStringFromData` missing `ST_MakeValid` (inconsistent with `PolygonFromData`)
**File:** `NARS/Infrastructure/SqlFragments.cs:34`

`PolygonFromData` wraps with `ST_MakeValid`; `LineStringFromData` did not. Degenerate linestrings (repeated identical points) could cause PostGIS to throw.

**Fix:** Added `ST_MakeValid(…)` wrapper to `LineStringFromData`. ✅

---

#### R2-4. Magic phase index `>= 3` in `loadFromDatabase`
**File:** `nars-vite/src/map.ts`

Hardcoded `3` for the `roads` phase index is fragile — a future phase reorder would silently break the `cityCenterMode` auto-set.

**Fix:** Replaced with `PHASES.findIndex(p => p.key === 'roads')`. ✅
