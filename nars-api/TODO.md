# NARS API TODO

## HIGH — Security & Data Integrity

- [x] **LIKE wildcard injection** — Escape `%`, `_`, `\` in user search strings before passing to `ILike` in `LocationSearchService.cs`
- [x] **Unbounded string/JSONB fields** — Add `[MaxLength]` to `FeatureBase.Data`, `ErrorLog.Message`, `ErrorLog.Context`, `LogEntry` fields, `FieldInspectRequest.Type/Status` to prevent OOM and storage abuse
- [x] **Password change without confirmation** — Make `CurrentPassword` required in `UpdateUserRequest` when `Password` is set; add current-password check to `UpdateAdminRequest`
- [x] **SegmentationClient OOM** — Cap error response body read in `SegmentationClient.cs` (use `StreamReader` with max length)
- [x] **No FK constraints on User location fields** — Add FK relationships for `User.CommuneId`, `User.DairaId`, `User.WilayaId` in `AppDbContext.OnModelCreating`
- [x] **UserCreationService masks DB errors** — Check `IsUniqueViolation` in catch block (`UserCreationService.cs:68-75`) instead of treating all `DbUpdateException` as duplicate username (409)
- [x] **Missing RefreshToken indexes** — Add indexes on `UserId` and `ExpiresAt` columns to avoid full table scans during pruning and user lookups
- [x] **Silent background task drop** — `BackgroundTaskQueue` should notify callers when work items are dropped (e.g., return `false` or log with correlation ID)

## MEDIUM — Performance

- [x] **N+1 admin overview query** — Replaced 6 sequential DB round-trips in `AdminOverviewService.GetNationalOverviewAsync` with a single CTE query (2 queries total: count + CTE)
- [x] **Security stamp DB query per request** — Cache the security stamp in distributed cache with short TTL instead of querying DB on every authenticated request (`AuthenticationExtensions.cs:94-103`)
- [ ] **Dummy BCrypt on every invalid login** — Consider a faster constant-time hash comparison for the dummy path to reduce DoS surface (~300ms CPU per unknown username) (deferred)
- [x] **Unbounded IN clauses** — All `Contains` calls are in EF Core LINQ queries; Npgsql already translates to `= ANY(@array)` automatically

## MEDIUM — Code Duplication

- [x] **Remove 14 redundant null-check boilerplate** — `[ApiController]` already returns 400 for null `[FromBody]` params; remove all `if (body is null) return Problem(...)` blocks across controllers
- [x] **Extract refresh token hash helper** — Deduplicate SHA256 hash computation in `RefreshTokenService.cs` (lines 25-27 and 98-100)
- [x] **Extract ADO.NET boilerplate** — Already well-factored: `EnsureOpenAsync`, `SqlFragments.AddParam`, `FeatureQueryHelper` cover the core pattern; remaining per-service code is inherently service-specific
- [x] **Remove primary constructor field shadowing** — Remove redundant `private readonly _db = db` fields in `DraftFeaturesService`, `ValidationService`, `CommuneScopeService`
- [x] **Extract scattered-refresh trigger** — Deduplicate `if (featureType == FeatureTypes.Area) QueueScatteredRefreshAsync(...)` in `FeaturesController.cs` (3 occurrences)

## MEDIUM — Structural

- [x] **Extract auth logic from PagesController** — Move `TryAuthenticateAsync`, `ValidateAccessTokenFromCookie`, `ValidateAccessTokenFromBearerHeader`, `TryRefreshSessionAsync` into `IPageAuthService`
- [x] **Move log sanitizer to service** — Extract `SanitizeLogField`/`SanitizeControlCharacters`/`SanitizeInto` (55 lines of stackalloc logic) from `LogsController.cs` into `ILogSanitizer`
- [x] **Split AuthController partial class** — Move `AuthorizedAdminSignup` to its own controller (`AdminSignupController`)
- [x] **Break up FeatureTypeRegistry** — Split 394-line god class into `FeatureTypeRegistry` (lookup), `FeatureCatalog` (UI metadata), `FeatureCleanupService` (DB deletion)
- [x] **Standardize response envelopes** — Merged 3 identical `{ success, id, message }` create DTOs (`SaveFeatureResponse`, `FieldInspectSubmitResponse`, `CreateEntranceResponse`) into unified `CreateResponse`; fixed DraftFeaturesController raw string errors → `Problem()`. Full `ApiResponse<T>` wrapper deferred (SPA frontend dependency)

## LOW — Inconsistencies

- [ ] **Route param casing** — Normalize `communeId` vs `wilaya_id` to one convention (breaking API change, deferred)
- [x] **CSP nonce injection** — Unify `<script>` vs `<script ` replacement patterns in `PagesController.cs` (lines 62, 90)
- [ ] **Error handling in FieldService** — `SubmitInspectionAsync` throws exceptions while all other services return result objects (already handled by global exception handler, low impact)
- [ ] **Consolidate commune DTOs** — Merge `CommuneItem`, `CommuneInfo`, `CommuneBoundaryResponse` into a shared base with optional fields (different contexts, not worth merging)
- [ ] **CSRF for anonymous endpoints** — Add guard that rejects anonymous POST mutations in `PipelineExtensions.cs` instead of silently skipping CSRF (intentional: login/admin-signup need anonymous POST)
- [x] **CORS localhost default** — Add startup validation that `Cors:AllowedOrigins` is set in non-development environments
- [ ] **Duplicate user summary DTOs** — Consolidate `UserInfo` and `AdminUserSummary` into a shared type (different contexts, not worth merging)
- [x] **Refresh token pruner dispose** — Scope disposal already handles DbContext; verified correct
- [x] **EntranceQueryService side validation** — Validate `side` parameter is "left" or "right" instead of silently defaulting to "right"
- [x] **Log level fallback** — Log null level as "unknown" instead of silently defaulting to "error" in `LogsController.cs:83`
