# Code Quality Issues — nars-api

Findings from the Aug 3 code review. Verified clean: build (0 warnings, analyzers +
TreatWarningsAsErrors), `dotnet format --verify-no-changes`, no async void/.Result/.Wait(),
parameterized SQL, CancellationToken forwarding, bounded pagination.

## High

- [x] **H1 — Geographic privilege escalation in `PUT /admin/users/{id}`**
  `nars-api/Controllers/AdminUserController.cs:143` — `ApplyRoleAndGeography` (line 250)
  writes `body.CommuneId`/`DairaId`/`WilayaId` directly to the target user with no
  caller-scope validation. `ValidateCreateUserScopeAsync` is only called on create paths.
  A `daira_admin` can assign a managed `commune_user` to any commune in the country.
  Fix: call scope validation before applying geographic changes.

## Medium

- [x] **M1 — Scope-validation failures map to 403 in admin signup**
  `nars-api/Controllers/AuthController.AdminSignup.cs:102` always returns 403 on
  `scopeResult.Error`; `AdminUserController.cs:49-51` correctly distinguishes Forbid()
  vs 400. Make error semantics consistent.

- [x] **M2 — Update-domain logic lives in the controller**
  `nars-api/Controllers/AdminUserController.cs:190-279` — `ValidateAdminUpdatePermissionAsync`,
  `ApplyUpdateFieldsAsync`, `ApplyRoleAndGeography` implement domain rules in the controller
  while `UserAuthorizationService` is a data-access facade (misleading name; also does user
  CRUD/deletion). Move update path into a service.

- [x] **M3 — String-matching to derive HTTP status codes**
  `AuthController.AdminSignup.cs:114` and `AdminUserController.cs:66` —
  `error?.Contains("already exists")` is fragile. Replace with a structured result
  (error-code enum / DuplicateField flag) from `UserCreationService.ValidateAndCreateUserAsync`.

- [x] **M4 — `ClearAllFeaturesAsync` loads all IDs into memory**
  `nars-api/Services/FeatureService.cs:104-121` — `Select(f => f.Id).ToListAsync` buffers
  O(total features) before `ExecuteDeleteAsync`; two round-trips per table. Use single
  `ExecuteDeleteAsync` per table (or RETURNING id).

- [x] **M5 — Inconsistent time source in `UpdatedAtInterceptor`**
  `nars-api/Infrastructure/UpdatedAtInterceptor.cs:9` uses `TimeProvider.System` while the
  app injects `IDateTimeProvider`. Use the injected clock everywhere.

- [x] **M6 — Inconsistent scattered-area recomputation strategy**
  `SpatialController.cs:101-113` runs the PostGIS recompute inline; `FeaturesController.cs:244-265`
  queues the same work on the background queue. Pick one strategy.

- [x] **M7 — Env passed as route parameter instead of injected**
  `nars-api/Controllers/LocationsController.cs:179,181` — `IHostEnvironment env` should be
  `IWebHostEnvironment` injected via constructor (controller doesn't inherit `NarsControllerBase`).

## Low

- [x] **L1 — Duplicated auth-cookie append logic (4 sites)**
  `AuthController.cs:130-131, 224-225`; `PagesController.cs:172, 210-211`. Extract a
  `CookieAuthWriter` helper.

- [x] **L2 — `AdminController.GetDaira` double-fetches the daira**
  `nars-api/Controllers/AdminController.cs:64-86` — scope check (`GetDairaByIdAsync`) then
  `GetDairaReportAsync` fetches it again (`AdminOverviewService.cs:121`). Reuse or fold in.

- [x] **L3 — Validation via exceptions in `LocationsController`**
  `nars-api/Controllers/LocationsController.cs:32-41, 56-66` — `ValidateSearch` throws
  `InvalidOperationException`; handlers catch to produce 400. Prefer direct `Problem(...)`
  like the rest of the app; prod hides the detail behind generic message.

- [x] **L4 — `GetUserFeatureCountsAsync` loads full User entities**
  `nars-api/Services/FeatureStatsService.cs:70-72` — materializes `password_hash`. Project
  with `.Select(u => new { u.Id, u.Username, u.Name, u.Email, u.Role })`.

- [x] **L5 — Email not normalized for uniqueness check**
  `nars-api/Services/UserCreationService.cs:30-32` — username lowercased, email not; case-
  differing emails rely on the DB unique-index race. Normalize email too.

- [x] **L6 — Case-sensitive type comparison in `FieldController`**
  `nars-api/Controllers/FieldController.cs:48` lowercases `type`; `SubmitInspection` (line 196)
  compares `ValidInspectionTypes` case-sensitively. Consistent handling.

- [x] **L7 — Failed-login recorded when locked user supplies correct password**
  `nars-api/Controllers/AuthController.cs:75-78` — extends lockout on correct password.
  Consider a distinct "refresh lockout" path or a comment.

- [x] **L8 — Misleading names**
  `AdminUserController.CreateAdmin/UpdateAdmin/DeleteAdmin` manage `commune_user`/`field_worker`
  too; `RefreshTokenService.FindUserByIdAsync`/`FindUserByUsernameAsync` — user lookup in a
  token service.

- [x] **L9 — Partial `CommuneInfo` when commune row missing**
  `nars-api/Controllers/AuthController.cs:159-162, 200-202` — non-null ID with null names
  embedded in response.

- [x] **L10 — Page handlers missing CancellationToken**
  `nars-api/Controllers/PagesController.cs:38, 51` — `Root` and `LoginPage` don't forward
  cancellation for file reads.
