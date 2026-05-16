# TODO

## Path to 9/10 — Project Hardening (Priority Checklist)

- [ ] **P0 — Make integration tests a required CI gate**: Run `NARS.Tests/Integration` in CI (PostgreSQL + PostGIS via Testcontainers), publish results, and block merge on failures.
- [ ] **P0 — Remove bootstrap default credentials**: Replace documented static admin password with one-time generated credentials (`make db-admin`) and update docs so no reusable defaults are shown.
- [ ] **P1 — Add deterministic cluster bootstrap + smoke test**: Keep `make cluster-up` fully idempotent with robust waits/retries and add a post-deploy smoke target that verifies `/health`, API auth, and frontend reachability.
- [ ] **P1 — Enforce stricter quality gates**: Treat backend warnings as errors in CI and enforce frontend minimum test coverage threshold so quality regressions fail fast.
- [ ] **P2 — Repo hygiene + supply-chain guardrails**: Remove accidental directories/artifacts, keep generated files untracked, and enable automated dependency/security checks (Dependabot + CodeQL + secret scanning).

## Infrastructure — Cluster & Data

- [x] Migrate from Docker Compose to k3s/kind cluster
- [x] Rename postgres → postgis across all k8s manifests
- [x] Build and push `nars-postgis` image (postgis/postgis:17-3.5 base)
- [x] Add PostGIS extension creation to init script
- [x] Add `MigrateAsync()` to Program.cs for auto-applying EF migrations
- [x] Suppress `PendingModelChangesWarning` in DbContext config
- [x] Fix PostgreSQL liveness/readiness probes timeout (1s → 5s)
- [x] Load reference data (58 wilayas, 557 dairas, 1541 communes)
- [x] Create `create_national_admin.sh` bootstrap script
- [x] Fix PostgreSQL WAL corruption after hard shutdown (`pg_resetwal`)

## Field Worker Role — Backend (NARS API) ✅ DONE

- [x] FieldWorker user role + hierarchy
- [x] Inspection system (model, controller, DTOs, migration)
- [x] Feature scoping by commune

## Field Worker Role — Frontend (nars-vite) ✅ DONE

- [x] FieldPanel, inspection forms, map click handler
- [x] Store, types, i18n, role-based routing

## Field Worker Role — Android (NARStreet) ✅ DONE

- [x] Role field, inspection API methods

## Admin Dashboard

- [x] National admin overview with scrollable wilaya grid
- [x] Wilaya drill-down with daira/commune breakdown

## nars-vite (Web frontend)

- [x] Integrate logging service

## Infrastructure

- [x] Push Docker images to registry
- [x] Deploy to k8s cluster (`make cluster-up`)
- [x] CI/CD pipeline

## Observability (LGTM Stack) ✅ DONE

- [x] Install kube-prometheus-stack (Prometheus + Grafana + AlertManager)
- [x] Install Loki (log aggregation)
- [x] Install Tempo (distributed tracing)
- [x] Install OpenTelemetry Collector
- [x] Configure Grafana datasources (Loki, Tempo, Prometheus)
- [x] Instrument NARS backend — traces (AspNetCore, HttpClient, EF Core) + metrics (Runtime, Hosting, Kestrel)
- [x] Instrument nars-vite frontend — traces (page load, fetch) via OTel Web SDK
- [x] Configure OTel pipelines: traces → Tempo, metrics → Prometheus, logs → Loki
- [x] Add ServiceMonitor for Prometheus scraping of OTel Collector

## Dashboard Capabilities ✅ DONE

- [x] Include field workers in admin overview stats (backend + frontend)
- [x] Add totals summary row per commune in stats tables
- [x] Add inline user creation button from dashboard

## User Type Test Coverage ✅ DONE

- [x] Add `UserRolesTests` — `field_worker` is not admin, `IsCommuneScoped` checks
- [x] Add `AdminControllerIntegrationTests` — all create-role combos tested (incl. commune\_user → field\_worker)
- [x] Add `AdminControllerIntegrationTests` — overview for every admin role + forbid for non-admins
- [x] Add `AdminControllerIntegrationTests` — wilaya drill-down and daira drill-down with scope enforcement

## Future / Nice-to-have ✅ DONE

- [x] Install metrics-server for HPA autoscaling
- [x] Add `docs/seed_reference_data.sql` to init process for fresh deployments
- [x] Generate proper EF migration to capture pending model changes
- [x] Replace hostPath PV with CSI-backed persistent volume for production
- [x] Add database backup cronjob in k8s

---

## NARStreet Cleanup — DONE

### Files removed (out of scope)
- [x] `domain/ComputeRoadDirectionsUseCase.kt` — road direction assignment (admin task)
- [x] `domain/SetHouseNumbersUseCase.kt` — auto-numbering (admin task)
- [x] `domain/GenerateNamingPanelsUseCase.kt` — panel generation (admin task)
- [x] `utils/RoadDirectionsCalculator.kt` — road direction implementation
- [x] `utils/HouseNumberingManager.kt` — auto-numbering implementation
- [x] `utils/NamingPanelGenerator.kt` — panel generation implementation
- [x] `data/model/FeatureTypes.kt` — types for unused phases
- [x] `ui/components/DrawingControls.kt` — empty placeholder
- [x] `utils/TlsUtils.kt` — mTLS infrastructure (out of scope)

### Pruned code blocks
- [x] `MapViewModel.kt` — removed 3 use case fields, methods, constructor params
- [x] `di/AppModule.kt` — removed 3 use case registrations, reduced MapViewModel to 3 deps
- [x] `FeatureStore.kt` — removed `referenceEntranceDbId`, `syncCounts()`, `getAllMainEntrances()`, simplified `FeatureCounts`
- [x] `NarsFeature.kt` — removed unused NarsFeatureType values, EntranceType, BuildingType, unused properties
- [x] `Config.kt` — removed `SNAP_PHASES`, `PHASE_COUNT`, analytics/crashlytics flags
- [x] `ApiUtils.kt` — removed unused phase branches, feature property mappings
- [x] `ApiService.kt` — removed `getCurrentUser()`, `refreshToken()`, `isAuthenticated()`
- [x] `SecurePreferences.kt` — removed P12 password methods
- [x] `SettingsViewModel.kt` — removed snap/grid/labels state
- [x] `SettingsScreen.kt` — removed snap/grid/labels UI
- [x] `NarsApplication.kt` — removed token refresh, `isLoggedIn()`, `logout()` methods
- [x] `SessionManager.kt` — removed `refreshSession()` method
- [x] `ContextMenuManager.kt` — removed compute directions menu item
- [x] `GeometryUtils.kt` — removed `calculateCentroid`, `isPointInPolygon`, `simplifyLine`, `calculateBoundingBox`, `BoundingBox`
- [x] `AppPreferences.kt` — removed snap/grid/labels keys
- [x] `BaseLayer.kt` — recreated with only `BaseLayerType` enum (removed duplicate URL constants)

### Fixed HIGH priority code quality issues
- [x] `build.gradle.kts` — removed default URLs and mTLS build config fields
- [x] `di/AppModule.kt` — `LogLevel.HEADERS` → `LogLevel.NONE`, removed duplicate timeouts
- [x] `ui/screens/LoginScreen.kt` — replaced `KoinJavaComponent.get()` with `koinInject()`
- [x] `modes/LabelAndMarkerManager.kt` — empty catch block now logs the exception
- [x] Haversine distance — consolidated to single `GeometryUtils` implementation (duplicate files removed)

---

## NARStreet Code Quality — Remaining Items

### HIGH
- [x] **No tests exist** — 60 unit tests added (Validation, FeatureStore, ApiUtils, GeometryConverter, SessionManager, ApiService)
- [x] **Manual JSON building everywhere** — Replaced with `kotlinx.serialization.json` builder API in `FeatureRenderer`, `NarsMap`, `ApiUtils`, `GeometryConverter`
- [x] **Manual JSON parsing in ApiService.login()** — Now uses `json.decodeFromString<LoginApiResponse>()` with `@Serializable` models
- [x] **Broad `catch (e: Exception)` / `runCatching`** — Replaced with explicit try/catch that logs, all API methods return `Result` types
- [x] **Heavy Android views in Compose state** — Removed unused `MapView`/`MapLibreMap` state variables from `MapScreen.kt`

### MEDIUM
- [ ] **Hardcoded strings throughout UI** — no `@StringRes` usage in any screen
- [x] **Hardcoded colors in LoginScreen** — Replaced all `Color(0xFF…)` with theme colors (`GlassBackground`, `PrimaryColor`, `DangerColor`, etc.)
- [x] **Settings.gradle relative path** — Now checks `geoman.dir` from `local.properties`, gradle property, system property, env var, then falls back to sibling path
- [x] **Inline JSON in NarsMap.getStyleJson** — Replaced raw string templates with `buildJsonObject { }` API
- [ ] **Flickering on feature update** — NarsGeoman clears and re-adds all features
- [x] **Duplicate phase logic in PhaseBar** — Extracted shared `computePhaseState()` function used by both `PhaseBar` and `CompactPhaseSelector`

### LOW
- [x] **PhaseBar color parsing** — Added `parsedColor` lazy property to `PhaseDefinition`, cached at class level
