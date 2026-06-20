# nars-api — Code Quality TODO

## Completed

- [x] **Consistent error responses** — All error responses now use `ProblemDetails` instead of anonymous `{ detail }` objects.
- [x] **Extract repository layer** — `IFeatureRepository` + `FeatureRepository` wraps all data access. Controller no longer depends on `AppDbContext` or raw ADO.NET.
- [x] **Consolidate ADO.NET helpers** — `SqlFragments.AddParam` is the single source of truth. Private duplicates in `FeatureQueryHelper` and inline in `ClearFeatures` removed.
- [x] **Cache SQL templates** — `BuildUnionAllCte()` runs once at startup. `_loadFeaturesSql`/`_loadByLayerSql` are cached `static readonly` fields.
- [x] **Reduce ClearFeatures round-trips** — 8 sequential `DELETE` queries consolidated into a single multi-CTE SQL batch.
- [x] **Validate `skip` parameter** — `Math.Max(skip, 0)` guards against negative values.
- [x] **Replace magic strings** — Claim names centralized in `ClaimNames` constants; `NarsControllerBase`, `JwtService` updated.
- [x] **Remove dead initializer** — `FeatureBase.CreatedAt` property initializer removed; set only in `FeatureTypeRegistry.CreateEntity`.
- [x] **Cache `GetAllTypes().Keys`** — `_allTypes`, `_allDescriptors`, `_allTableNames` are cached `static readonly` lists computed once.
- [x] **Document CSRF cookie trade-off** — Inline comment explains `HttpOnly = false` for SPA token reading.
- [x] **Narrow CSP `connect-src`** — Removed broad `http:` scheme-wide allow; kept specific domains and dev addresses.
- [x] **Cache `MaxFeatureDataSize`** — Parsed once in constructor field instead of on every property access.

## Remaining

- [ ] **RESTful URLs** — Endpoints use RPC-style paths (`/api/save`, `/api/load`, `/api/clear`) instead of resource-based (`/api/features`, `DELETE /api/features`). Breaking change — plan a versioned transition.
- [ ] **DTO validation** — `FeatureUpdateRequest.Data` is `JsonElement?` with no `[Required]` or validation attributes. Consider FluentValidation or data annotations.
- [ ] **Singleton review** — Services registered as `Singleton` that capture scoped state via factory. Verify none hold per-request state.
- [ ] **AuthController.AdminSignup race test** — Requires real DB with unique constraints; `InMemory` cannot trigger `DbUpdateException`.
- [ ] **ValidationService DB-level optimizations** — `ST_MakeValid` repetition, uncached `DbCommand` (inner-database concern).
