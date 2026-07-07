# Code Quality Issues

## 🔴 High
- [x] **Unify error response envelope** — Standardized on `Problem(detail:, statusCode:)` across all controllers.
- [x] **Authorization default case** — `ValidateCreateUserScopeAsync` now returns error for unknown role combos.
- [x] **No concurrency control** — Added `[Timestamp]` to `FeatureBase`.

## 🟠 Medium
- [x] **Missing XML docs** — Added `<summary>` to all controller actions and service interface methods.
- [x] **Monolithic extension methods** — `AddNarsServices` split into 10 focused sub-methods.
- [x] **Raw SQL fragility** — Extracted magic timeout to named constant.
- [x] **DTO envelope inconsistency** — Removed `ActionResponse`, unified on `ApiResponse`.
- [x] **Missing foreign keys** — Added `[ForeignKey]` attributes to `FeatureBase.UserId`, `HouseEntrance.RoadId`, `Inspection.UserId`.

## 🔶 Low
- [x] **Hard-coded CSP** — Now configurable via `CspOptions` / `appsettings.json:Csp`.
- [x] **Dead code** — Removed `DetailResponse`.
- [x] **Magic numbers/strings** — Extracted JWT min length (`32`) and delete timeout (`30`) to constants.
- [x] ~~**JSON column converter**~~ — Skipped. Current `string` + `jsonb` is appropriate for varying GeoJSON-like data; EF Core converter would require a rigid model.
- [x] ~~**ConfigureAwait(false)**~~ — Skipped. No analyzer enforces CA2007; no sync context in ASP.NET Core, so zero benefit.
