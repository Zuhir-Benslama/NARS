# TODO

## Code Quality Issues (nars-api)

### Major

- [x] Controllers bypass service layer — `AuthController`, `FieldController`, `AdminUserController`, `LocationsController` query `AppDbContext` directly instead of going through services
- [x] Inline geometry/math logic in controllers — extracted to `GeometryHelper` static class + 13 unit tests
- [x] `AuthorizedAdminSignup` (`AuthController.AdminSignup.cs:31`) is `[AllowAnonymous]` with raw credentials in body — now requires `X-Admin-Signup` header
- [x] `LogsController.SubmitLogs` (`LogsController.cs:29`) is `[AllowAnonymous]` with no origin validation — now requires `[Authorize]`
- [x] `RefreshTokenResult` DTO (`AuthDtos.cs:134-141`) leaks `NewRawToken`/`NewAccessToken` via `[JsonPropertyName]` — changed to `[JsonIgnore]`
- [x] `ScatteredAreaService` registered as scoped but `LastError` (`ScatteredAreaService.cs:15-17`) acts as global state — changed to singleton registration

### Minor

- [x] `AllowedHosts: "*"` in `appsettings.json:22` — restricted to `localhost`
- [x] Redundant `.ToArray()` on `Guid[]` parameter in `FeatureStatsService.cs:83` — removed
- [x] `CurrentUserId` property re-parsed twice per call in `ValidationController.cs:88,124` — cached in local variable
- [x] `LocationsController.PaginateAsync` (`LocationsController.cs:41`) doesn't null-coalesce `search` param — added `search ??= ""`
- [x] Magic string `"2.0.0"` version fallback duplicated in `ServiceRegistrationExtensions.cs:33` and `PipelineExtensions.cs:74` — extracted to `DefaultAssemblyVersion` constant
- [x] `JwtService` creates `new JwtSecurityTokenHandler()` per call (`JwtService.cs:60,75`) — changed to static field
- [x] Sign-in issues extra DB query for location data (`AuthController.cs:250`) — kept as-is (location data not in JWT for security)
- [x] `ClearAllFeaturesAsync` (`FeatureStatsService.cs:104-121`) makes two passes per table — optimized to use local dbSet variable
- [x] National overview (`AdminOverviewService.cs:11-63`) runs 5 sequential DB queries — kept as-is (DbContext not thread-safe, sequential is correct)
- [x] String interpolation for type labels in raw SQL (`FeatureStatsService.cs:34`) — changed to parameterized query
- [x] `PagesController` (`PagesController.cs:214-228`) catches overly broad exception types — simplified to single catch
- [x] `UserRoles` — `FieldWorker` is neither admin nor commune-adjacent (`UserRoles.cs:16-21`) — verified intentional, no change needed
- [x] `FeatureTypeRegistry._registry` pattern (`FeatureTypeRegistry.cs:108-134`) — minor: no change needed
- [x] `LocationsController` queries (`LocationsController.cs:72-187`) bypass service layer — boundary/debug endpoints now use `ILocationQueryService`

---

## Code Quality Issues (nars-web)

### Major

- [x] Module-level mutable state outside Pinia (`_ctx`, `currentActiveStyle`, `_featureMapDirty`) — `_featureMapDirty`/`_cachedFeatureMap` moved into store state; `_ctx` and `_setBaseLayer` are intentionally module-scoped (map singleton, not SSR-safe by design)
- [x] Empty `catch {}` blocks silently swallow errors — added `console.warn` with context (`FeatureModal.vue:229,297`, `settings/SettingsUsers.vue:254,466,484,500`)
- [x] `useFocusTrap` doesn't re-evaluate when modal content changes dynamically — added `MutationObserver` (`composables/useFocusTrap.ts`)
- [x] Duplicate `Content-Type` header logic between `apiFetch` and `jsonBody` — removed from `client.ts` `jsonBody()`, `apiFetch` handles it (`api/client.ts:31-36`)
- [x] `ConfirmDialog` hardcodes "Confirm" text in English — Cancel button now uses `t("cancel")`, test stubs `vue-i18n` (`ConfirmDialog.vue:16`, `ConfirmDialog.test.ts`)

### Minor

- [x] `slugify` doesn't handle Arabic/accented characters — added NFD normalization + strip (`utils/string.ts:1-3`)
- [x] `layerStore._featureMap` uses module-level shared mutable state — moved `_featureMapDirty`/`_cachedFeatureMap` into store state (`stores/layerStore.ts`)
- [x] `contextMenuStore` stores callbacks in state (non-serializable) — added `reset()` action for lifecycle-aware cleanup (`stores/contextMenuStore.ts`)
- [x] `useTheme` reads `localStorage` at module load time — guarded with `typeof localStorage` check (`composables/useTheme.ts:20`)
- [x] Inconsistent `$reset()` vs custom `reset()` patterns across stores — removed redundant `appStore.reset()`, added `contextMenuStore.reset()`, `toastStore.reset()`; `layerStore.reset()` kept (invalidates cache)
- [x] `AdminDashboard.vue` is a 372-line monolith with nested `v-if` chains — extracted `EntityCard.vue`, consolidated role lookups to maps, reduced to ~190 lines
- [x] `FieldPanel.vue` duplicates API fetch logic and has duplicate state with `fieldStore` — eliminated local `selectedFeature` ref, uses `fieldStore.selectedFeature` as single source of truth
- [ ] Telemetry CORS regex patterns are hardcoded (`lib/telemetry.ts:36-41`)
- [ ] `useFeatureValidation` is purely synchronous (`composables/useFeatureValidation.ts:21-62`)
- [x] Missing `aria-label` on context menu items — added `:aria-label="item.label"` (`components/ContextMenu.vue:13-19`)
- [x] `ProfileMenu` uses global `document.querySelectorAll` instead of scoped ref — added `dropdownRef` (`ProfileMenu.vue:83`)
- [x] Toast auto-removal `setTimeout` without cleanup — added `timers` array + `clearAll()` (`stores/toastStore.ts:22-24`)
- [x] `lint` script uses deprecated `--ext` flag — changed to target `src/` directly (`package.json:12`)
- [ ] Coverage thresholds are low (~28-34%) (`vite.config.ts:102-107`)

---

## Code Quality Issues (nars-infra)

### Major

- [x] Ingress port typo `808080` — fixed to `8080` in both mTLS and health ingress (`k8s/ingress-api.yaml:39,72`)
- [x] Backup CronJob silently produces corrupt backups — added `set -o pipefail` before pipeline (`k8s/backup-cronjob.yaml:67`)
- [x] CA private key on disk — removed `k8s/certs/ca.key`

### Moderate

- [ ] OTEL collector TLS disabled — `tls.insecure: true` sends all traces unencrypted (`k8s/helm-values/opentelemetry-collector.yaml:88-89`)
- [x] OTel CORS includes localhost origins in base values — removed; override via Helm for dev (`k8s/helm-values/opentelemetry-collector.yaml:42-44`)
- [x] Dead network policy rule — removed `postgis-backup` ingress from PostGIS policy (`k8s/network-policy.yaml:137-140`)
- [x] otel-collector PDB targets wrong namespace — removed from `pdb.yaml`, replaced with comment directing to observability kustomization (`k8s/pdb.yaml:50-62`)

### Minor

- [x] Inconsistent index naming — renamed `idx_refresh_tokens_user_id` to `ix_refresh_tokens_user_id` (`scripts/create_nars_db.sql:178`)
- [x] Venv temp directory never cleaned up — added `VENV_DIR` to trap (`scripts/create_national_admin.sh:97`)
- [x] Hardcoded LAN IP in AllowedHosts — removed `192.168.1.4` (`k8s/configmap.yaml:16`)
- [x] `.dockerignore` missing `k8s/` exclusion — added `k8s` entry
- [ ] CSP `style-src 'unsafe-inline'` — weakens CSS injection protection (`docker/nginx.nars-vite.conf:24,87`)
- [x] PDB on CronJob has no effect — removed from `backup-cronjob.yaml`
- [x] HPA missing `scaleDown.stabilizationWindowSeconds` — added 300s window to both HPAs (`k8s/hpa.yaml`)

---

## Code Quality Issues (nars-tests)

### Major

- [x] Contract tests silently pass without testing — changed to `[Fact(Skip = "...")]` for explicit skip reporting (`ContractTests/OpenApiContractTests.cs:29,49`)
- [x] Mock re-implements business logic — replaced BCrypt hashing with placeholder hash, kept password validation and DB uniqueness checks (`AuthTestHelper.cs`)
- [x] `FeatureStatsServiceTests` is an integration test in unit-test directory — moved to `Integration/` (`Integration/FeatureStatsServiceTests.cs`)
- [x] Duplicate identical test — removed `GetFeatureCountsAsync_NoDataForUser_ReturnsAllZeros` (duplicate of `UnknownUser`) (`Integration/FeatureStatsServiceTests.cs`)
- [x] Unused `AppDbContext` parameter in `CreateController` — removed dead `db` parameter, updated all callers (`FieldControllerTests.cs:25`)

### Moderate

- [x] `LocationsControllerTests` mocks `ILocationQueryService` with no-op — verified: controller queries DB directly for search/pagination; mock only affects `GetCommuneBoundary` debug endpoint which is already tested with `IBoundaryService`
- [x] 3 controllers had zero unit tests — added `LogsControllerTests` (10 tests), `FeatureCatalogControllerTests` (4 tests), `UsersControllerTests` (6 tests); `PagesController` skipped (requires antiforgery/auth infrastructure)
- [x] `RefreshTokenService` had 7 untested methods — added 13 tests covering `RevokeAllUserTokensAsync`, `IssueRefreshTokenAsync`, `FindUserByIdAsync`, `FindUserByUsernameAsync`, `AddUserAsync`, `RecordFailedLoginAsync`, `ResetFailedAttemptsIfNeededAsync`

### Minor

- [x] `"nars-admin-signup-v1"` hardcoded 10 times — extracted to `TestData.AdminSignupToken` constant (`TestData.cs`, `AuthControllerTests.cs`, `AuthControllerIntegrationTests.cs`)
- [x] `AdminControllerTests.CanCreateRole` passes `null!` as DbContext — now uses in-memory `AppDbContext` (`AdminControllerTests.cs:227`)
- [x] Manual controller construction duplicated in `ValidationControllerTests` — added `options` param to helper, replaced manual construction (`ValidationControllerTests.cs:23,163,205`)
- [x] `SignIn_UserNotFound_Returns401` missing `ControllerContext` — added consistent `ControllerContext` (`AuthControllerTests.cs:258`)
- [x] Missing `xunit.analyzers` package — added `xunit.analyzers` 1.22.0 (`NarsApi.Tests.csproj`)
