# NARS API TODO

Code quality issues found during review of the API project (build clean,
0 warnings; 501 tests pass; `dotnet format --verify-no-changes` clean). The
High/Medium items below have all been fixed; Low items remain open.

## High

- [x] **`[Authorize(Roles = ...)]` always returns 403** (`Infrastructure/AuthenticationExtensions.cs:32`)
  - JWT role claims are written as `"role"` (`Services/JwtService.cs:42`) and kept verbatim via `MapInboundClaims = false`, but `TokenValidationParameters.RoleClaimType` is never set, so it defaults to the `ClaimTypes.Role` URI. `[Authorize(Roles=...)]` evaluates via `IsInRole` against that URI → no match → **403 for every user**.
  - Affected endpoints: all of `Controllers/FieldController.cs:15` plus `GET /api/admin/wilaya/{id}` and `GET /api/admin/daira/{id}` (`Controllers/AdminController.cs:45,58`). The rest of the app reads the raw `"role"` claim via `NarsControllerBase.CurrentUserRole` and works fine.
  - **Evidence:** `nars-tests/AuthenticationExtensionsTests.cs:164` asserts the validated principal has no `ClaimTypes.Role` claim. No test exercises the middleware authorization pipeline, so this slipped through. The `CanReviewFeatures` policy (`AuthenticationExtensions.cs:96-100`) reads `ClaimNames.Role` directly and is the working pattern.
  - **Fix:** set `validationParams.RoleClaimType = ClaimNames.Role;` (and optionally `NameClaimType = ClaimNames.Username`) in `AuthenticationExtensions.cs`, or replace the `[Authorize(Roles=...)]` attributes with claim-based policies like `CanReviewFeatures`.
  - ✅ Add a test that exercises the middleware authorization pipeline (or a unit test asserting `RoleClaimType`).

## Medium

- [x] **District-adjacency SQL: missing parentheses silently skip the urban-area check** (`Services/ValidationService.cs:157-166`)
  - `AND` binds tighter than `OR`, so the query is `ST_Touches(...) OR (ST_Intersects(...) AND EXISTS(...))` — the `EXISTS(urban area intersecting both)` gate only applies to the boundary-intersection branch. A district that merely **touches** any existing district passes without any shared urban area, contradicting the error message "must connect to an existing district in this urban area" (`Controllers/ValidationController.cs:132-136`).
  - **Fix:** parenthesize the disjunction: `(ST_Touches(...) OR ST_Intersects(...)) AND EXISTS(...)`.

- [x] **Lockout and password change do not invalidate existing access tokens** (`Services/RefreshTokenService.cs:50-56`, `Controllers/UsersController.cs:42`)
  - Only refresh tokens are revoked; access tokens live 60 min (`JwtOptions.ExpiresInMinutes`), so a locked-out account keeps using a valid access token until expiry, and a reset password does not terminate already-issued sessions.
  - **Fix:** short TTL is only partial mitigation; add a per-user token-version/`security_stamp` claim checked on each request, or centrally revoke access for sensitive changes.
  - ✅ **Done:** added `security_stamp` column + claim; rotated on lockout and password change; re-validated against the DB in `OnTokenValidated` on every authenticated request (migration `20260810062821_AddUserSecurityStamp`).

- [x] **`UserProfileService` maps any `DbUpdateException` to "duplicate username"** (`Services/UserProfileService.cs:99-103`)
  - Catches all `DbUpdateException` (including the email unique-constraint race and unrelated DB failures) and returns `DuplicateUsername`, producing a misleading 409 and masking real errors.
  - **Fix:** inspect the PostgreSQL error (`PostgresException.Number == 23505` / constraint name) to distinguish which column collided; rethrow otherwise.

- [x] **Refresh-token reuse after rotation is not detected** (`Services/RefreshTokenService.cs:35-40`)
  - A revoked token returns the generic "invalid or expired" result with no family revoke. If a stolen refresh token is used to rotate, the victim's subsequent refresh just fails silently and the attacker keeps the new session. Practical exposure is limited (HttpOnly + SameSite=Lax + Secure cookies), but the rotation scheme exists precisely to provide this replay detection.
  - **Fix:** on replay of a revoked token, revoke all outstanding tokens for the user (or at least log an alert).

## Low

- [x] **Refresh tokens never pruned** (`Services/RefreshTokenService.cs:63-68`)
  - Every rotation inserts a new row; revoked/expired rows accumulated forever. **Fix:** added `Services/RefreshTokenPruner.cs` — a `BackgroundService` that deletes `revoked OR expires_at <= now` on a configurable interval (`RefreshTokenPruning:IntervalHours`, default 24h; `RefreshTokenPruningOptions`), registered in `AddNarsDomainServices`. Failed runs are logged and retried next tick.

- [x] **"Pages" cookie scheme is dead config** (`Infrastructure/AuthenticationExtensions.cs`)
  - `AddCookie("Pages", ...)` was registered but nothing referenced the scheme (`PagesController` uses `[AllowAnonymous]` + manual `jwt.ValidateToken`). **Fix:** removed the registration (and its unused `Microsoft.AspNetCore.Authentication.Cookies` using). `AuthenticationExtensionsTests.PagesCookieScheme_IsConfiguredSecurely` replaced with `NoLegacyPagesCookieScheme_IsRegistered`, which asserts the scheme stays absent while JWT bearer remains registered.

- [x] **`Jwt:Algorithm` config is silently ignored** (`appsettings.json`, `Infrastructure/AppOptions.cs`, `Services/JwtService.cs`)
  - The config key had no corresponding `JwtOptions` property and `JwtService` hardcoded `HmacSha256`. **Fix:** added `JwtOptions.Algorithm` with an HS256/HS384/HS512 allowlist enforced by `[RegularExpression]` at startup (options are `ValidateOnStart`). `JwtService` now signs with the configured algorithm and sets `ValidAlgorithms` on validation; the JwtBearer pipeline sets an HS* `ValidAlgorithms` allowlist to close algorithm-confusion swaps. The existing `"Algorithm": "HS256"` in `appsettings.json` is now honored.

- [x] **`total_count` re-read per row** (`Infrastructure/FeatureQueryHelper.cs:135`)
  - The same `total_count` (constant from the CTE) was assigned on every iteration of the read loop. **Fix:** read once on the first row.

- [x] **Mixed Guid schemes** (`Models/AiDraftFeature.cs:50`)
  - Uses `Guid.NewGuid()` (v4) while everything else uses `Guid.CreateVersion7()`. **Fix applied (round 1):** now `Guid.CreateVersion7()`.

- [x] **`DraftFeaturesService` bypasses `IDateTimeProvider`** (`Services/DraftFeaturesService.cs:91,184,188`)
  - **Fix applied (round 1):** injected `IDateTimeProvider` (wired via DI).

- [ ] **Mixed `DateTime` vs `DateTimeOffset` + timezone-less columns**
  - User/RefreshToken/Feature timestamps are `DateTime` mapped to `timestamp without time zone`, while `AiDraftFeature` uses `DateTimeOffset` (timestamptz); `RefreshTokenService.cs:143` mixes `utcNow.UtcDateTime` with direct `DateTime` comparisons. Works on UTC hosts. **Status:** deliberately deferred — standardizing on `DateTimeOffset` is a schema-wide change (column type migration across several tables) with no functional defect on the UTC-deployed hosts. Documented here rather than half-applied.

- [x] **Entrance label has no length validation** (`DTOs/FieldDtos.cs:14-18`)
  - **Fix applied (round 1):** added `[param: MaxLength(500)]` matching `HouseEntrance.Label`.

- [x] **Locked sign-in is reported as 401, admin sign-up as 423** (`Controllers/AuthController.cs:61-64`, `Controllers/AuthController.AdminSignup.cs:62-64`)
  - **Status:** kept as-is and deliberately documented in `AuthController.SignIn`. The public sign-in endpoint returns one generic 401 for every failure (enumeration resistance for the main login surface); admin sign-up reports 423 because that endpoint already proves ownership of an admin account, so revealing lock state leaks nothing. The asymmetry is intentional.

## Not an issue (checked and confirmed sound)

- **No SQL injection:** all raw SQL uses allowlist-validated table names (`FeatureTypeRegistry.ValidateTableName`) + Npgsql parameters (FeatureQueryHelper, ValidationService, FieldService, RefreshTokenService, BoundaryService, ScatteredAreaService).
- **No mass assignment:** DTOs map to entities explicitly; `UserId`/scope always from token claims.
- **No sync-over-async / `.Result` blocking; no swallowed exceptions** in request paths (the broad `catch` in `FeatureService.cs:142` is a background worker that logs intentionally).
- **No hardcoded secrets:** `${NARS_DB_PASSWORD}`/`NARS_JWT_SECRET` env placeholders, fail-fast startup validation.
- CSRF middleware, CSP nonce, cookie hardening, constant-time signup-token compare, and BCrypt dummy-hash timing defense are all correctly implemented.

## Makefile (build tooling)

- [x] **Undefined-variable check never failed** — `infra-lint-makefile` piped through `|| true`, so a typo'd `$(VAR)` printed a warning but `make infra-lint` still passed. Now fails on match and prints `✓` when clean.
- [x] **`db-migrate-nars` hardcoded `nars_db`** — now uses `$(DB_NAME)` like every other DB target.
- [x] **SQL migrations never ran in the deploy chain** — `nars-infra/migrations/*.sql` (idempotent, e.g. `0001_create_ai_draft_features.sql`) were only applied manually. `kustomize-apply` now runs `db-migrate-nars` after the EF baseline, so fresh clusters get every migration and future ones can't be silently missed.
- ✅ Verified: `make infra-lint` passes (exit 0), `make -n db-migrate-nars`/`kustomize-apply` render the expected commands, and the undefined-vars check fails on a synthetic `$(UNDEFINED_VAR_TEST_XYZ)` Makefile.
