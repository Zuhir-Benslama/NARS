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

- [ ] **Refresh tokens never pruned** (`Services/RefreshTokenService.cs:63-68`)
  - Every rotation inserts a new row; revoked/expired rows accumulate forever. Add a periodic cleanup (`DELETE FROM refresh_tokens WHERE revoked OR expires_at < now()`).

- [ ] **"Pages" cookie scheme is dead config** (`Infrastructure/AuthenticationExtensions.cs:82-90`)
  - `AddCookie("Pages", ...)` is registered but nothing references the scheme (`PagesController` uses `[AllowAnonymous]` + manual `jwt.ValidateToken`). Remove it or actually use it.

- [ ] **`Jwt:Algorithm` config is silently ignored** (`appsettings.json`, `Infrastructure/AppOptions.cs:16-20`, `Services/JwtService.cs:34`)
  - The config key has no corresponding `JwtOptions` property and `JwtService` hardcodes `SecurityAlgorithms.HmacSha256`. Remove the config key or wire it up with an allowlist.

- [ ] **`total_count` re-read per row** (`Infrastructure/FeatureQueryHelper.cs:135`)
  - The same `total_count` (constant from the CTE) is assigned on every iteration of the read loop. Read it once before the loop.

- [ ] **Mixed Guid schemes** (`Models/AiDraftFeature.cs:50`)
  - Uses `Guid.NewGuid()` (v4) while everything else uses `Guid.CreateVersion7()` (`FeaturesController.cs:68`, `FieldService.cs:144,185`, `LogsController.cs:91`). v4 destroys index locality for the draft queue.

- [ ] **`DraftFeaturesService` bypasses `IDateTimeProvider`** (`Services/DraftFeaturesService.cs:91,184,188`)
  - Uses `DateTimeOffset.UtcNow` directly, unlike the rest of the codebase; also widens the DateTime/DateTimeOffset split (see next). Inject the provider for testability and consistency.

- [ ] **Mixed `DateTime` vs `DateTimeOffset` + timezone-less columns**
  - User/RefreshToken/Feature timestamps are `DateTime` mapped to `timestamp without time zone`, while `AiDraftFeature` uses `DateTimeOffset` (timestamptz); `RefreshTokenService.cs:143` mixes `utcNow.UtcDateTime` with direct `DateTime` comparisons. Works on UTC hosts, but a non-UTC host skews lockout/expiry. Standardize on `DateTimeOffset`.

- [ ] **Entrance label has no length validation** (`DTOs/FieldDtos.cs:14-18`)
  - `FieldEntranceCreateRequest.Label` lacks `[MaxLength]`, unlike `FeatureSaveRequest.Label` (`[MaxLength(500)]`). A huge label hits the DB unwarned.

- [ ] **Locked sign-in is reported as 401, admin sign-up as 423** (`Controllers/AuthController.cs:61-64`, `Controllers/AuthController.AdminSignup.cs:62-64`)
  - Sign-in discards `CredentialCheckStatus.Locked` and returns "Invalid username or password", while admin sign-up returns 423. Align (or deliberately document the 401 for enumeration resistance).

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
