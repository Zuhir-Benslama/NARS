# Code Quality Issues

## 🔴 High
- [x] **Login timing side-channel** — Lockout check now happens after BCrypt verify (using dummy hash for unknown users).
- [x] **DbUpdateException silently swallowed** — Exceptions are now logged via `logger.LogWarning`.

## 🟠 Medium
- [x] **Duplicated authorization logic** — Extracted into `IUserAuthorizationService` / `UserAuthorizationService`.
- [x] **Controllers inject DbContext directly** — `LogsController`, `SpatialController`, `ValidationController`, and `UsersController` migrated to service injection. `LocationsController` kept as-is (DbContext usage is encapsulated in generic pagination helper). `AuthController`, `AdminController`, `FieldController` partially extracted (complex, need dedicated service layer in future).

## 🔶 Low
- [x] **Inconsistent field naming** — Renamed `MaxFeatureDataSize` → `_maxFeatureDataSize`.
- [x] **TOCTOU race in catch block** — Now returns generic conflict message without re-querying DB.
- [x] **Null commune scope bypass** — Added explicit `.HasValue` check in `FieldController.cs`.
- [x] **Redundant DTO validation attributes** — Removed `[Required]` in favor of `[JsonRequired]`.
- [x] **WKT string duplication** — Extracted shared `AppendWktCoords` helper.
- [x] **Cache policy limited** — Cache now serves all non-search paginated requests, not just skip=0/take=500.
