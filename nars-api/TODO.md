# nars-api — Code Quality TODO

All items have been addressed. The issues marked "Remaining" below are cosmetic/design decisions that would break the API contract or are inherently integration-level concerns.

## Completed

### Bugs Fixed
1. **FeaturesController** — HttpContext values now captured into locals before background task queueing (prevented `ObjectDisposedException`)
2. **SpatialController** — `JsonElement.GetProperty` → `TryGetProperty` with proper 400 response
3. **FieldController** — Null-safe `CommuneId` comparison; locked road owner check added

### Code Quality - Structure
4. **FeatureStatsService + FeatureQueryHelper** — DRY: eliminated ~100 lines of duplicated SQL generation (FeatureStatsService now delegates to FeatureQueryHelper)
5. **FeatureStatsService** — Removed unused `CommandTimeoutSeconds` and `config` parameter
6. **AdminOverviewService** — Replaced in-memory table loads with targeted grouped queries
7. **FeatureDtoConverter** — Returns typed `FeatureDto` instead of `object`
8. **ClearFeatures** — Uses `DELETE ... RETURNING id` raw SQL (one round-trip per table instead of two)

### Code Quality - Safety
9. **AuthController.AdminSignup** — `SaveChangesAsync` wrapped in `try-catch (DbUpdateException)` for race condition safety
10. **ValidationService** — Table name fallbacks now throw `InvalidOperationException` (no silent fallback)
11. **PasswordValidator** — Type annotation updated for immutability

### API Consistency
12. **AuthController/PagesController** — All `access_token` cookies use `jwt.AccessTokenExpiresIn` (respects `Jwt:ExpiresInMinutes` config). Added `AccessTokenExpiresIn` to `IJwtService`.
13. **Response DTOs** — All endpoints now return typed DTOs instead of anonymous objects (`ActionResponse`, `CreateAdminResponse`, `LoadFeaturesResponse`, `FieldInspectionsResponse`, `FieldInspectSubmitResponse`, `CreateEntranceResponse`)

### Testing
14. Added 8 new tests covering:
    - SpatialController missing coordinates property
    - FieldController commune null/equal/different/mismatch edge cases
    - FieldController locked/unlocked road owner
    - FeaturesController area saves trigger background refresh
    - FeaturesController non-area saves don't trigger background refresh

## Remaining

### Would break API contract
- **DTO JSON naming inconsistency** — Mix of `snake_case` (`[JsonPropertyName]`) and `camelCase` (global policy)
- **CommuneInfo misnamed** — Used for Wilaya/Daira in responses

### Integration-level only
- **ValidationService DB-level optimizations** — `ST_MakeValid` repetition, uncached `DbCommand` (inner-database concern)
- **AuthController.AdminSignup race test** — Requires real DB with unique constraints; InMemory cannot trigger unique violation `DbUpdateException`
