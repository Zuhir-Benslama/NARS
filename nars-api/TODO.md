# NARS-API TODO

## Code Quality Issues (2026-07-10)

### High Priority

- [x] Cookie `Secure` flag relies on request scheme (`NarsControllerBase.cs:64`) — auth cookies could be sent over plain HTTP if forwarded headers are missing. Fix: use `CookieSecurePolicy.Always` in production.
- [x] Missing account lockout on admin-authorized-signup (`AuthController.AdminSignup.cs:31-42`) — lockout check happens after BCrypt verify. Fix: check `LockedUntil` before password verification.
- [x] Duplicated user-creation logic between `AdminController.cs:98-181` and `AuthController.AdminSignup.cs:40-150`. Fix: extract shared `IUserCreationService`.
- [x] CSP `'unsafe-inline'` in style-src (`AppOptions.cs:68`) — weakens XSS protection. Fix: use nonce-based enforcement like the existing script nonce pattern.

### Medium Priority

- [x] Null-forgiving operators on JSON parsing (`SpatialController.cs:51-52`) — crashes on malformed road data with no meaningful error. Fix: add explicit null checks or try-catch returning 400.
- [x] FeatureStatsService opens N parallel DB connections for count queries (`FeatureStatsService.cs:11-24`, `41-52`). Fix: single UNION ALL query instead of N DbContexts.
- [x] AdminController is a god class — 411 lines, 8 endpoints + 4 helpers (`AdminController.cs`). Fix: split into `AdminController` (overview) and `AdminUserController` (CRUD).
- [x] `Guid.Parse` without try-catch on untrusted DB data (`FeatureQueryHelper.cs:119-124`). Fix: use `Guid.TryParse` with descriptive error.
- [x] N+1 round-trips in `ClearAllFeaturesAsync` (`FeatureRepository.cs:98-132`). Fix: collect IDs in a single UNION ALL, then batch delete.
- [x] PagesController.TryRefreshSessionAsync only catches `DbUpdateException` and `InvalidOperationException` (`PagesController.cs:187-230`). Fix: add general `catch (Exception)`.
- [x] RefreshTokenService starts transaction before null check (`RefreshTokenService.cs:28-35`). Fix: move transaction start to after token validation. *(Deferred: `FOR UPDATE SKIP LOCKED` requires an active transaction)*
- [x] ScatteredAreaService.LastError is not thread-safe (`ScatteredAreaService.cs:15`). Fix: use `volatile` or `Interlocked`.
- [x] AllowedHosts set to `"localhost"` only (`appsettings.json:22`). Fix: ensure production override sets the correct domain.
- [x] PUT `/api/user/update` uses redundant "update" suffix and singular `/user` (`UsersController.cs:18`). Fix: use `PUT /api/user/profile` or `PATCH /api/user`.
- [x] FeatureCatalogController route uses inconsistent nesting (`FeatureCatalogController.cs:129`). Fix: use `GET /api/features/by-layer/{layerType}`.
- [x] BackgroundTaskQueue does not wait for in-flight tasks on shutdown (`BackgroundTaskQueue.cs:64-78`). Fix: give in-flight tasks a grace period before cancellation.

### Low Priority

- [x] `ValidationController.CheckCoordinateBounds` is non-static (`ValidationController.cs:192`). Fix: mark as static.
- [x] Missing `[ProducesResponseType]` for 429 on rate-limited endpoints across all controllers.
- [x] `GET /api/features` bulk DELETE uses DELETE with a body (`FeaturesController.cs:104-123`). Fix: use `POST /api/features/clear`.
- [x] Single-letter uppercase local variables `A`, `B`, `C` (`ValidationController.cs:61-63`). Fix: rename to `a`, `b`, `c` or descriptive names.
- [x] FeatureTypeRegistry uses `Dictionary` instead of `FrozenDictionary` for static lookup (`FeatureTypeRegistry.cs:106-133`). Fix: use `FrozenDictionary.ToFrozenDictionary()`.
- [x] BoundaryService uses synchronous `using` instead of `await using` (`BoundaryService.cs:14`). Fix: change to `await using`.
- [x] Null-forgiving operators on nullable model properties in LocationsController ILike calls (`LocationsController.cs:72-73`). Fix: use null-coalescing or make properties non-nullable.
- [x] SpatialController.GetRoadSide has unbounded while loop (`SpatialController.cs:99-103`). Fix: add upper bound.
- [x] UpdatedAtInterceptor uses `DateTime.UtcNow` directly instead of `IDateTimeProvider` (`UpdatedAtInterceptor.cs:16`). Fix: inject `IServiceProvider` or document the limitation.
- [x] ValidationController instance helper methods should be static (`ValidationController.cs:205-239`). Fix: mark as static.
- [x] LogsController anonymous log submission lacks field sanitization (`LogsController.cs:28-29`). Fix: added `SanitizeLogField` to strip control characters and enforce length limits.
