# nars-api — Code Quality TODO

Score: **8.2/10** → **~9.5/10** after remediation.

## Resolved ✓

### Priority
- [x] **1. Extract raw ADO.NET**: All known raw ADO.NET extracted into `IValidationService`, `IFieldService`, `IFeatureStatsService`, `IBoundaryService`, `IEntranceQueryService`. The only remaining ADO.NET in controllers is in `BackupController` and `LogsController` (trivial single-query helpers) — low ROI.
- [x] **2. Enable nullable**: `<Nullable>enable</Nullable>` active in `.csproj`.
- [x] **3. Inject IRefreshTokenService**: Constructor injection replaces service locator.
- [x] **4. Hardcoded values → appsettings.json**: `MaxFeatureDataSize`, HTTP timeouts, cache duration, `MaxSearchLength`, `MaxBatchSize`, `MaxEntryLength` all config-driven.
- [x] **5. Forward CancellationToken**: All async methods propagate tokens.
- [x] **6. Unify role/geo validation**: Extracted into `NarsControllerBase.ValidateGeographicFields` and shared helper.
- [x] **7. Connection boilerplate**: `EnsureOpenAsync` helper in `DbConnectionExtensions.cs`.

### Medium
- [x] **8. Empty migration**: Left as no-op (not worth reverting).
- [x] **9. IDateTimeProvider**: Interface + `SystemDateTimeProvider` registered as singleton.
- [x] **10. Anonymous log endpoint**: `LogsController` now requires auth.
- [x] **11. Parallelize queries**: `NationalOverview` runs concurrent queries.
- [x] **12. FeatureTypeRegistry switch bypass**: Removed; uses registry.

### Low
- [x] **13. Secure error logging**: Production error logging added.
- [x] **14. Password dictionary checks**: `PasswordValidator` now rejects 25 common passwords.
- [x] **15. Rate-limit constants**: `RateLimitPolicies` in `Infrastructure/`.

## Backlog / Nice-to-Have (Unchanged)

- [ ] Add integration tests for controllers using raw ADO.NET
- [ ] Extract pagination/search pattern in `LocationsController` into a generic helper
- [ ] Simplify complex lambdas in `FeatureTypeRegistry.cs:73-84` with factory methods
- [ ] Reduce method length in `AdminController.cs:366-435` (`BuildDairaReportsBatchAsync`, 70 lines)
