# NARS API TODO

Code quality issues found during review (build clean, 0 warnings; 410 unit + 91 Postgres-backed tests passing). Grouped by severity.

## Medium

- [x] **`ErrorLog.Code` truncated to 100 but column is `varchar(50)`** (`Controllers/LogsController.cs:94`)
  - **Fix:** truncated `Code` at 50 (`MaxCodeLength`) matching `ErrorLog.Code [MaxLength(50)]`.

- [x] **`FieldEntranceCreateRequest.Label` has no `MaxLength`** (`DTOs/FieldDtos.cs:17`)
  - **Fix:** added `[param: MaxLength(500)]` matching `HouseEntrance.Label [MaxLength(500)]`.

## Low

- [ ] **Security-stamp DB round-trip on every request** (`Infrastructure/AuthenticationExtensions.cs:75-101`)
  - `OnTokenValidated` re-reads the user's `SecurityStamp` from the DB on every authenticated request. Deliberate tradeoff for instant token revocation.
  - **Status:** kept as-is — caching would relax the revocation guarantee; the behavior is documented in code.

- [x] **Dead code**
  - Removed: `RateLimitPolicies.Api`, `LocationQueryService.GetAllWilayasAsync`/`GetDairasByWilayaAsync`/`GetCommunesByDairaAsync` (+ interface), `FeatureQueryHelper.AddParameter`, `SqlFragments.AddParam(Guid[])`.
  - Kept (verified in use): `RefreshTokenService.TimeProvider` (used by test subclass), `FeatureDtoConverter.IsoDateFormat` (used by `SpatialController`/`FeatureQueryHelper`).

- [x] **`DraftFeaturesService` uses `DateTimeOffset.UtcNow` directly** (`Services/DraftFeaturesService.cs`)
  - **Fix:** injected `IDateTimeProvider` (wired via DI; test helper updated).

- [x] **`AiDraftFeature.Id` uses `Guid.NewGuid()`** (`Models/AiDraftFeature.cs:50`)
  - **Fix:** now `Guid.CreateVersion7()` matching the codebase convention.

- [x] **Inconsistent stored-XSS hygiene** (`Controllers/LogsController.cs`)
  - **Fix:** all text log fields (Code, Message, Context, Url, Method, UserAgent) now share one `SanitizeLogField` (control-char strip + HTML-encode + truncate); encoded output is re-truncated to `maxLen` so columns can't overflow after entity expansion.

- [x] **Password `[MaxLength(128)]` exceeds BCrypt's 72-byte limit** (`DTOs/AuthDtos.cs`)
  - **Fix:** capped password fields at `MaxLength(72)`; `PasswordValidator` now rejects >72 UTF-8 bytes. Test updated for the new contract.

- [x] **Manual `CreatedAt`/`UpdatedAt` stamps duplicate the interceptor** (`Controllers/FeaturesController.cs`)
  - **Fix:** dropped the manual stamp; `FeatureTypeRegistry.CreateEntity` `createdAt` param is now optional (`default`), letting `UpdatedAtInterceptor` stamp both on `SaveChanges`.

- [x] **`BuildUnionAll` interpolates table names without `ValidateTableName`** (`Services/FeatureStatsService.cs`)
  - **Fix:** table names now routed through `FeatureTypeRegistry.ValidateTableName`.

- [x] **`UsersController` has no explicit null-body guard** (`Controllers/UsersController.cs:22-24`)
  - **Fix:** added explicit null-body guard (400), matching sibling controllers.

- [x] **Mixed authorization style**
  - `Controllers/FieldController.cs` used class-level `[Authorize(Roles = UserRoles.FieldWorker)]` while `Admin`/`AdminUser` controllers had no role attributes and enforced roles only inside `UserAuthorizationService`.
  - **Fix:** unified on declarative role gates + in-service scope checks (roles are static and declarative; geographic/target scope is data-dependent and stays in the service):
    - `Infrastructure/UserRoles.cs`: added `AnyAdmin` and `UserManagementRoles` combined constants.
    - `Controllers/AdminController.cs`: `Overview` now `[Authorize(Roles = UserRoles.AnyAdmin)]` (role switch still handles geo-scope + missing-scope cases).
    - `Controllers/AdminUserController.cs`: class-level `[Authorize(Roles = UserRoles.UserManagementRoles)]` — field workers now get a consistent 403 from middleware instead of a mix of 403/empty-200 per endpoint.
    - `Controllers/FieldController.cs`: unchanged (already declarative).

- [x] **`LocationsController` doc comment advertises caching that isn't implemented**
  - **Fix:** removed the false caching claim from the doc comment. `CacheOptions.ReferenceDataDurationHours` left in place (referenced by bootstrapping option-validation tests).

- [x] **`GetUsedEntranceNumbersAsync` materializes all entrance numbers** (`Services/EntranceQueryService.cs`)
  - **Fix:** query now filters by the requested side's parity in SQL (`left`→odd, `right`→even), which `SuggestEntranceNumber` provably never consults for the other parity — halves transferred data. `side` param added to interface/controller/test mocks.

## Verification

- `dotnet build --no-restore`: 0 warnings / 0 errors.
- `dotnet format Workspace.sln --verify-no-changes`: clean.
- 410 unit tests + 91 Postgres-backed service tests: all passing.
