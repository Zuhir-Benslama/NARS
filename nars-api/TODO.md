# Code Quality — Fixed

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Medium | `AllowedHosts: "*"` overly permissive — pinned to `localhost` | `appsettings.json:22` | Fixed |
| Medium | Redundant `FindAsync` DB round-trips — replaced with JWT claims in Admin/Field controllers | AdminController, FieldController | Fixed |
| Medium | `List<object>` / `object Features` return types — replaced with typed `FieldFeatureResult` + generic `LoadFeaturesResponse<T>` | `FieldService.cs:12`, `FeatureDtos.cs:75` | Fixed |
| Medium | `RefreshTokenResult` contained `Models.User?` — replaced with `Username` string | `AuthDtos.cs:132` | Fixed |
| Low | Inconsistent JSON naming — added snake_case attributes to `ActionResponse` and `RefreshTokenResult` | `AuthDtos.cs:127,132` | Fixed |
| Low | Dead code: `ParseIntConfig` method removed | `SqlFragments.cs` | Fixed |
| Low | Suppressed migration warning `PendingModelChangesWarning` removed | `DatabaseExtensions.cs:22` | Fixed |
| Low | Inconsistent claim type — `LogsController` now uses `ClaimNames.UserId` | `LogsController.cs:46` | Fixed |
| Low | Magic numbers (2048, 10, 500) extracted as constants | `LogsController.cs` | Fixed |
| Low | Anonymous log submission — rate limited to 30 req/min (already adequate) | `LogsController.cs:24-28` | Kept |
| Medium | PagesController `IsAuthenticatedAsync` consolidated — extracted helpers, sets `HttpContext.User` after cookie validation | `PagesController.cs:117` | Fixed |
| Medium | ScatteredAreaService `Lock` removed — unnecessary synchronization for status property | `ScatteredAreaService.cs:21` | Fixed |
| Low | `IBackgroundTaskQueue` moved from `Infrastructure/` to `Services/` for consistency with other interfaces | `BackgroundTaskQueue.cs` | Fixed |
| Low | `[HttpPost("field/entrance/create")]` → `[HttpPost("field/entrance")]` — consistent RESTful naming | `FieldController.cs:180` | Fixed |
| Low | `TryGetDescriptor` added to `FeatureTypeRegistry` for clearer null-check pattern | `FeatureTypeRegistry.cs:127` | Fixed |
| Low | Removed `LockedUntil` check in `CreateEntranceFromInspection` — account lockout shouldn't block feature operations | `FieldController.cs:214` | Fixed |
