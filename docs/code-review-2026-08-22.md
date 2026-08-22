# Code Review — nars-api + nars-roads (2026-08-22)

Full review of `nars-api` (.NET) and `nars-roads` (Python segmentation
service): build, test suites, security posture, performance, and dead code.
All findings fixed unless marked otherwise.

**Verification (nars-api):** `dotnet build Workspace.sln` clean · 507/507
tests pass · `dotnet format --verify-no-changes` clean.

**Verification (nars-roads):** ruff check/format clean · yamllint clean ·
mypy + pytest green in the rebuilt `nars-roads:test` image · 72/72 tests,
99.6% coverage (CI floor: 85%).

## Medium

| ID | Finding | Fix |
|----|---------|-----|
| M1 | Admin role/scope changes did not rotate the user's `SecurityStamp`, so outstanding JWTs kept working until expiry; stamp cache eviction existed but nothing invalidated across nodes | `UserAuthorizationService.UpdateManagedUserAsync` rotates the stamp, evicts the cache entry, and revokes refresh tokens on any role/scope change; regression-tested |
| M2 | Lockout race: lockout was checked before recording a failure, letting parallel attempts exceed the threshold; failure counter reset while locked | `VerifyCredentialsAsync` checks lockout first, records failure after; `RecordFailedLoginAsync` ignores attempts while locked, starts a fresh cycle after expiry, resets at threshold; tested |
| M3 | Missing/localhost-only CORS origins were allowed in Production with only a warning | `AddNarsCors` throws at startup in non-Development when origins are missing or all localhost; warning path removed |

## Low

| ID | Finding | Fix |
|----|---------|-----|
| L1 | JWT signing algorithm not validated against HS256/384/512 map | `AddNarsJwtAuthentication(algorithm)` validates and pins `ValidAlgorithms`; wired from config |
| L2 | Over-generous token lifetimes (24 h access, 30 d refresh) | Defaults tightened to 60 min / 7 days |
| L3 | Feature pagination ran the CTE twice per page request (2 round trips) | `BuildSql` emits both statements per round trip; `FieldService` consumes via `NextResultAsync`; offset-past-last-row regression test added |
| L4 | Per-request user/road/draft lookups materialized tracked entities | `AsNoTracking()` added to read paths |
| L5 | Duplicated LIKE-wildcard escaping logic | Shared `SqlFragments.EscapeLikeWildcards`; both call sites use it |
| L6 | Dead code: tile-proxy/satellite HttpClients, `HttpClientOptions`, unused config knobs, oversized default page size | Removed; default feature page size 1000 → 500 |
| L7 | Logging redaction cap of 10 KB could log large bodies | Default and appsettings reduced to 4 KB |
| L8 | `SegmentationClient` let raw exceptions escape its error contract | JSON parse / property extraction wrapped into `SegmentationServiceException` with inner exception |
| L9 | Single DB-backed health endpoint served liveness **and** readiness — DB outage restarted healthy pods | Added `/health/live` (dependency-free) and `/health/ready` (DB-aware); k8s probes split accordingly (`app-deployment.yaml`); `/health` + `/api/health` kept for startup probe and external monitoring ingress (CIDR-restricted) |
| L10 | Security-stamp cache invalidation is per-node; other replicas served stale stamps up to TTL after rotation | Postgres trigger (`migration AddStampEvictionNotifyTrigger`) fires `pg_notify('nars_stamp_evict', userId)` on `security_stamp` change; `StampEvictionListener` (BackgroundService) LISTENs and evicts locally, with reconnect loop; no external dependency added |
| L11 | Login CSRF: unauthenticated POSTs bypassed antiforgery, so a cross-site form could log a victim into an attacker's account | Origin validation for unsafe methods on `/api`: present `Origin` must match an allowed origin or the request origin, else 403 (absent Origin = non-browser client = allowed); skipped in Development like antiforgery; pure decision function unit-tested |

## Deliberately kept

- `AiDraftFeature.TypeRoad` / `StatusEdited` constants referenced by frontend.
- Anonymous `/health` endpoints — aggregate status string only; monitoring
  ingress restricted to cluster CIDRs.

## nars-roads

| ID | Finding | Fix |
|----|---------|-----|
| M1 | Non-ASCII byte in `X-Internal-Token` reached the app latin-1-decoded; `secrets.compare_digest(str, str)` raises `TypeError` on non-ASCII → unauthenticated 500 + traceback | Token comparison runs on UTF-8 bytes; bad token is a clean 401; regression-tested with a raw-bytes header |
| M2 | `mask_to_polygons` scanned the full mask per label (`labeled == region_id`) — O(regions × H×W); a dense tile could stall a request for seconds while holding an inference permit; also broke for regions filling their bbox (uniform crop has no 0.5-crossing) | Regions iterated via `scipy.ndimage.find_objects` bounding-box slices; crops padded one background pixel so solid footprints contour correctly; coordinates offset back to absolute space; disjoint-blob regression test added |
| L1 | Local `nars-roads:test` image predated the scikit-image 0.26 bump (had 0.24, where `max_size=` is invalid) — `make roads-test` failed at mypy locally | Image rebuilt from current requirements; in-container mypy + pytest green |
| L2 | Dead condition: `if not transform` in `_embedded_transform` (Affine is always truthy) | Removed; identity equality remains as the sole "no usable transform" test |
| L3 | emptyDir volumes (`/tmp`, weights) had no `sizeLimit` — runaway torch/GDAL caches or a huge checkpoint could fill node disk | `sizeLimit: 2Gi` on both |

Deliberately fine: module-level TestClient without lifespan (intentional),
unauthenticated `/health` + `/ready` (ClusterIP-internal only), 30 s
semaphore-timeout → 503 (documented), validation ordering (cheap checks first).

## nars-web

Gates at completion: `vue-tsc` clean, eslint + prettier clean, stylelint
clean, **932/932** unit tests (baseline 954; net −22 from deleted dead-code
test suites, plus new regression coverage listed below).

| ID | Finding | Fix |
|----|---------|-----|
| M1 | On 401 the api client redirected straight to login; an expiring access token logged users out mid-edit, and parallel in-flight 401s each triggered their own refresh/redirect | Single-flight `refreshSession()` shared with boot; one replayed request per call; redirect is idempotent and error-log flush fires once (`api/index.ts`, `main.ts`); 5 new tests (replay success, single-flight, still-401 → redirect) |
| M2 | Log flush on unload used `sendBeacon`, which cannot send `X-CSRF-Token`; the server's antiforgery middleware rejected every authenticated final batch (403 / 401 on `[Authorize]` LogsController) — pending logs silently lost on every page close | `pagehide` (fires on mobile Safari/bfcache too) + `fetch({keepalive:true})` with CSRF header; batch tail-trimmed under the 64 KB keepalive budget (`MAX_KEEPALIVE_BYTES`); 3 new tests |
| M3 | `connect-src 'self' http: https: …` — scheme wildcards defeated CSP containment; any injected script could exfiltrate anywhere | Enumerated hosts only: `'self' data:` + tile CDNs (from config's `tileUrls`) + localhost dev/HMR entries; telemetry OTLP is same-origin by default. login.html tightened to same-origin-only incl. img-src |
| M4 | `renderScatteredAreas` reset `_scatteredPolygons = []` per call but was invoked once PER scattered feature during load — N>1 scattered features clobbered each other until only the last was hit-testable | Split into `clearScatteredAreas()` (once per load) + `addScatteredArea()` (append); loader-db calls clear before the feature loop; accumulation regression tests in geometry.test.ts + loader-db.test.ts |
| M5 | Login page displayed raw `data.detail` from sign-in responses — leaked internal details (lockout reasons, upstream identity errors) to unauthenticated users | Status-mapped generic messages (401/400 → invalid credentials, 429 → too many attempts, else generic retry), localized en/fr/ar via existing STRINGS table; `data.detail` never rendered |
| L1 | Dead code (~10 symbols + a whole feature): PDF export module (`exportMapToPdf` + html2canvas/jspdf optionalDeps + EXPORT_CONFIG + i18n keys), `updateDrawingPreview` + drawing-preview source/layer, `hasUndo`/`getUndoLabel` + undoStore getters, `closestOnSegment`, `isSnappingActive`, `buildPopupContent`, `createEntranceIconHtml`, `POLYLINE_WIDTH`, `refreshScatteredAreas`, `ScatteredRefreshResponse` type | All removed with their test suites; html2canvas/jspdf dropped from package.json |
| L2 | FeatureData→GeoJSON mapping duplicated ×4 (undo, edit-commit, loader-build, ctx-menu) with drift — edit-commit rendered a radius-less circle's coords as closed LineString where undo made a Polygon; city-center styling hardcoded ×3; radius rule duplicated between modal validation and draw-save toast path | Canonical `featureDataToGeometry(data, kind)` in feature-data.ts used by all four; `CITY_CENTER_COLOR` in phases.ts + `CITY_CENTER_CONFIG.ringStrokeWidth`; shared `cityCenterRadiusError()` in lib/city-center.ts |
| L3 | Delete/undo asymmetry: while a deleted road/main entrance sat on the undo stack its dependents' dangling `roadDbId`/`mainEntranceDbId` were repaired on restore — but stack eviction (100-entry cap) made the delete permanent and left references dangling forever | Eviction-time detach in `recordDelete`: overflowing entry's dependents are cleared locally and persisted via PUT, mirroring the restore-repair persistence pattern |
| L4 | `commune_user → field_worker` delegation branch unreachable: Users tab gated behind `isAdminUser`, but the server's `UserManagementRoles` explicitly includes commune_user | New `canManageUsers` getter mirroring server policy; SettingsModal gates the Users tab on it. App.vue/router admin gates unchanged (dashboards stay admin-only) |
| L5 | Boot fragility: `/api/current_user` fetched twice (checkAuth + loadUserAndCommune); hardcoded English init-failure toast | Second fetch only when the store has no user; `app_init_failed` i18n key added (en/fr/ar) |
| L6 | Logger shipped full URLs including query strings (`?search=…` user input) to the error-log pipeline | `stripUrlQuery` on the url field AND url/method removed from the free-form context blob so the query can't leak via JSON.stringify |
| L7 | Minors | edit-commit: readGeomanGeometry failure now aborts the commit with an error toast instead of silently PUTting stale pre-edit data; map-init: concurrent base-layer switch queued latest-wins (awaitable) instead of dropped, keeping UI active-style state honest; ProfileMenu logout guarded against double-submit |

### Deliberately kept

- `pointInMunicipalLimit` / `pointInScatteredArea` have no production caller
  today, but they are the only readers of hit-test state that live loaders
  actively populate (`displayCommuneBoundary`, scattered features). Deleting
  them would turn that state write-only and gut the only observability for
  boundary/scattered loading; removal would be a product decision (drop the
  whole subsystem) rather than a lint fix.
- Server endpoint `POST /api/areas/refresh-scattered` (SpatialController) now
  has no client consumer after `refreshScatteredAreas` removal — candidate for
  server-side follow-up if the scattered-area workflow is not returning.

### Known behavior changes

- Radius-less circle with vertex coordinates now commits as Polygon everywhere
  (was split LineString/Polygon across code paths).
- A concurrent base-layer switch now applies after the in-flight one completes
  instead of being ignored.
- readGeomanGeometry failure surfaces "geometry save failed" and aborts the
  commit instead of saving stale data.

## nars-infra

Gates at completion: yamllint clean · hadolint clean · shellcheck clean ·
ruff clean · schema convergence proven in a throwaway PostGIS container:
init script → migration applied twice → exactly one of each index /
constraint / FK, zero duplicates.

| ID | Finding | Fix |
|----|---------|-----|
| M1 | Schema drift between `scripts/create_nars_db.sql` §10 (runs automatically at Docker image init) and `migrations/0001_create_ai_draft_features.sql` (re-applied to any cluster by `make db-migrate-nars`; both idempotent **by name**): `idx_*` vs `ix_*` index prefixes, named vs auto-named CHECKs, inline vs explicitly named FKs, and `ON DELETE NO ACTION` vs `RESTRICT`/`SET NULL` — so every fresh bootstrap that later ran migrations got **duplicate indexes** | `0001` aligned to the init script: `ix_ai_draft_status`, `ix_ai_draft_created_at`, `chk_ai_draft_feature_type/confidence/status/geometry_matches_type`, explicit `ai_draft_features_commune_fk` (RESTRICT) and `ai_draft_features_reviewed_by_fk` (SET NULL); mutual "names must stay in sync" warnings added to both files; verified end-to-end in-container |
| M2 | `/health` routed publicly via the `nars-frontend` ingress (and both rule blocks of `nars-frontend-local`) with no `whitelist-source-range` — defeated the CIDR-restricted dedicated `nars-api-health` ingress, and the SPA never calls `/health` | Path removed from all frontend ingress rules; external monitoring uses `api.nars.dz/health`; `make smoke-test` still passes via the host-less `nars-api-local` ingress |
| L1 | `otel-metrics-service.yaml` comment claimed verification against chart "0.159.0" while the Makefile pins `OTEL_COLLECTOR_VERSION=0.169.0` | Selector re-verified directly against the 0.169.0 chart templates (`app.kubernetes.io/component: standalone-collector` under `mode: deployment`); comment now defers to the Makefile variable instead of a hardcoded version |
| L2 | `.hadolint.yaml` DL3018 override said gnupg was "intentionally unpinned" while `Dockerfile.nars-backup` pins `gnupg=2.4.9-r1` | Comment corrected; override kept as a warn-level safety net for future utility images |
| L3 | `nars-roads` pod had no startupProbe (every other workload does): model loading slower than ~110 s would trip liveness and restart-loop the pod | startupProbe on `/ready` (5 s period × 60 failures ≈ 5 min budget) gates readiness/liveness |

Also cleaned: empty header-only `nars-infra/TODO.md` deleted (unreferenced —
the root README table lists TODOs only for nars-api/web/roads);
`curlimages/curl:8.10.1` digest-pinned to its multi-arch manifest list
(`sha256:d9b454…`), the last tag-only image ref in the repo.

Note for databases provisioned before M1 was fixed: they may still carry
duplicate `idx_*` indexes next to `ix_*`. Dropping an index is safe
(rebuildable), but per data-safety rules inspect first
(`\d ai_draft_features`, `\di ix_ai_draft*`) and confirm before dropping.

Deliberately kept: `secret.yaml` remains a documentation-only template;
PostGIS root-init chown pattern; hostPath/local-path storage clearly marked
dev-only; OTLP→Tempo `tls.insecure=true` with an inline production runbook;
GPG passphrase delivered as CronJob env var.

## nars-tests

Gates at completion: `dotnet build Workspace.sln --no-restore` 0 warnings
(TreatWarningsAsErrors) · full suite **511/511 green** (~23 s; 507 at phase
start — see count math below).

| ID | Finding | Fix |
|----|---------|-----|
| H1 | `ai_draft_features` review flow (ReviewedBy/ReviewedAt, second-review guard, concurrent double-submit race) had zero test coverage — and could not be tested because the table exists in the EF model but in **no migration** (deliberately owned by `nars-infra/migrations/0001_create_ai_draft_features.sql`, applied by `make db-migrate-nars`) | New `Service/DraftFeaturesServiceTests.cs`: accept/reject stamps reviewer + timestamp, second review → AlreadyReviewed keeping first decision, concurrent double-review race (two services over one DB, exactly one Success), unknown draft → NotFound. `NarsDatabaseFixture.InitializeAsync` now applies the infra SQL after `MigrateAsync()` via `FindInfraSqlPath` (walks up from `AppContext.BaseDirectory`); stale comment in DraftFeaturesTests fixed. Do NOT add an EF migration for this table |
| M1 | RefreshToken concurrency test minted all tokens through one DbContext (serializes, tests nothing) | Each of 5 concurrent calls gets its own context via a `CreateInMemoryDbPair` factory (`MintAccessTokenAsync_ConcurrentPageLoads_AllSucceed`) |
| M2 | Three UsersControllerTests tautologies asserted the seeded row equals itself; "valid update" test never verified what reached the service | Tautologies deleted; `ValidUpdate_Returns200` now Moq-Verifies the controller forwards the exact `UpdateUserRequest` record untouched |
| M3 | Real user ids/stamps hardcoded as literals in AdminUserControllerTests + AuthenticationExtensionsTests | `SeedData.CreateUserAsync` gained optional `id`/`username`/`securityStamp` params; 13 literals collapsed onto named constants |
| M4 | AuthController/AdminSignupController wiring duplicated across AuthControllerTests and Service/AuthControllerServiceTests | Shared factories in `AuthTestHelper`: `CreateAuthController(db)`, `CreateAdminSignupController(db)` (no ControllerContext attached); call sites delegate |
| M5 | `JwtService_RejectsTamperedToken` / `JwtService_IssuesOpaqueRefreshTokens` duplicated verbatim in AuthenticationExtensionsTests | Duplicates removed; canonical versions verified present in JwtServiceTests |
| M6 | LocationsController boundary 404 test conflated two failure modes (unknown commune vs missing geometry) | Split: shallow `UnknownCommune_Returns404` + deep `BoundaryMissingWithoutCoordinates_Returns404` (commune exists without lat/lng) |
| M7 | FeatureCatalog clamp test asserted the result only — clamped arguments reaching the service were unverified | Adds `Verify(LoadByLayerAsync(userId, layer, 0, 500, ...), Times.Once)` |
| M8 | ScatteredArea consecutive-errors test duplicated a cache test and froze the clock so the 5-min dedup window was untested | Rewritten: same key twice with an advancing clock (`SetupGet(...).Returns(() => now)` — note `Mock.Of<T>(x => x.Prop == captured)` freezes at setup time); asserts timestamp == T0+5min; dup test removed |
| M9 | 16 sites hand-rolled `JsonDocument.Parse` (leak-prone) and InfrastructureServicesTests re-implemented InMemory options | `TestData.ToJsonElement(string/T)` (+ `using var persistedData`) and `TestData.CreateInMemoryDb(prefix, interceptor?)`; local helper deleted |
| L1 | `CleanTablesAsync` truncated `__EFMigrationsHistory` too | Excluded from truncation |
| L2a | JWT scheme assertion was a weak Contains/DoesNotContain pair | `Assert.Single(schemes)` + name equality |
| L2b | Security-stamp **cache-hit** branches untested everywhere (all suites stubbed `GetStampAsync → null`) | Two new AuthenticationExtensionsTests: hit+matching stamp succeeds with an empty DB (proves no lookup), hit+mismatched stamp fails |
| L2c | `WhitespacePassword_ReturnsError` was `Assert.NotNull` | Asserts the exact "at least 8 characters" message |
| L2d | `GetScatteredStatus` only tested the no-error path | New `GetScatteredStatus_RecordedError_IsReported` via strict `IScatteredAreaService` mock; asserts constant message + ISO timestamp |
| L2e | GeometryHelperTests `used.Add(10000)` / `.Add(10001)` added cross-parity numbers the walk never probes (`MaxEntranceNumber` is 100_000) | Dead lines removed |
| L3a | ServiceProvider leaks: `RunOnTokenValidatedAsync` helper and BackgroundQueueProcessorTests never disposed providers | `await using var sp` on the auth helper; processor tests now `DisposeAsync` after every `StopAsync` |
| L3b | Production leak surfaced by those tests: `StopAsync` cancelled `_shutdown` but never disposed it, and `DisposeAsync` disposed **without cancelling** — a Start→Dispose sequence left the worker spinning forever on a disposed CTS | `DisposeAsync` now delegates to `StopAsync` when started, then disposes the CTS (production fix in `BackgroundTaskQueue.cs`, regression-guarded by the tests above) |
| L4a | DtoValidationTests picked `GetConstructors().First()` — non-deterministic if extra ctors appear | Primary ctor selected by most parameters |
| L4b | `ExpectStartupFailure`'s failure message rendered null exceptions as bare text | Reports type name or "no exception" explicitly |
| L4c | `MissingDbPassword_Throws` depended on ambient appsettings for the connection string | Injects the `${NARS_DB_PASSWORD}` placeholder connection string via `UseSetting` — hermetic like the sibling Jwt tests |
| L5a | Unused SSH.NET PackageReference | Removed (verified zero usage first) |
| L5b | Empty header-only TODO.md stub | Deleted |
| L5c | EscapeLikeWildcards duplicate backslash case (InlineData + separate Fact) | Dup Fact removed, InlineData kept |
| L5e | `"old-hash"` magic string where BCrypt-format hash is required | Uses `DummyPasswordHash` |
| L5f | `AuthTestHelper.CreateJwtService` public with unused null-default param (zero external callers) | Private, required param |
| L5g | RefreshTokenServiceTests stubbed `AccessTokenExpiresIn` which RefreshTokenService never reads | Dead setup removed |
| L5h | Six 12-line `AuthorizedAdminSignupRequest` literals in AuthControllerTests | `TestData.ValidAdminSignup(...)` builder with scenario-relevant overrides |
| L5i | PagesControllerTests lock comment claimed cross-process safety a lock cannot provide | Comment reworded: intra-host serialization + create-if-missing convergence |
| L5j | CacheControl tests had zero `.woff2` rows though production fingerprint-matches them | Hashed woff2 → immutable, un-hashed → no-cache rows added |
| L5k | Malformed stamp-eviction payload test passed even if LogWarning silently broke | Warning verified Times.Once containing payload; valid/empty cases verify Times.Never |

Test-count math from the 507 baseline: +5 (H1) − 3 (M2) − 2 (M5) − 1 (L5c)
+1 (M6 split) − 1 (M8 dedup) = 506 after the earlier batches; +2 (L2b) +1
(L2d) +2 theory rows (L5j) = **511** now.

Note: the fixture change (H1) means integration test classes now require
`nars-infra/migrations/0001_create_ai_draft_features.sql` to exist relative to
the repo root — keep both files in sync per the nars-infra M1 finding.

## Makefile + make/

Gates at completion: `make infra-lint` fully green (all 8 targets incl.
tag-guard and local-ingress self-tests) · Tempo values proven byte-identical
via `helm template` diff · tag-rewrite awk proven byte-identical via diff on
real `kubectl kustomize` output · every digest-pinned image pull-tested ·
forced-fallback run through make (`PATH=/usr/bin:/bin`) green · hostile
IMAGE_TAG rejected by guard before any recipe executes.

| ID | Finding | Fix |
|----|---------|-----|
| M1 | proxy-up advertised `http://localhost:8080/api/health`; the real route is `/health` (dedicated `nars-api-health` ingress; smoke-test checks `/health`) | Hint corrected |
| M2 | Tempo configured via ~14 inline `--set` flags while prometheus/loki/otel use linted values files; `memBallastSizeMbs=128` was dead config | Extracted to `nars-infra/k8s/helm-values/tempo.yaml`, now covered by yamllint; dead flag dropped; `helm template` render proven identical |
| M3 | Tool images unpinned or untagged: `hadolint/hadolint` (**no tag**), `koalaman/shellcheck:stable` (moving), `node:22-alpine`, `alpine/socat` (untagged ×2), YAMLLINT/RUFF tag-only | All centralized as Makefile vars and tag+digest-pinned (hadolint v2.15.1, shellcheck v0.11.0 — matches local, socat 1.8.1.3); both socat recipes now share `SOCAT_IMAGE` |
| M4 | `.env` (secrets) written with default umask (~644), chmod 600 only at end | `umask 077` first line of the recipe |
| L1 | AGENTS.md claimed cluster name `nars-cluster`; Makefile uses `nars` | Doc corrected |
| L2 | cluster-stop's `exists=$(kubectl get … && echo true)` captured the whole deployment table; worked only by accident | Proper `>/dev/null 2>&1 &&` conditional |
| L3 | Image-tag rewriting done by an opaque inline awk one-liner inside kustomize-apply (unlintable, uncommented) | Extracted to documented `nars-infra/scripts/kustomize-tag-rewrite.awk`, invoked via `-f`; diff-proven equivalent |
| L4 | kustomize-apply silently skipped password-sync/baseline/migrations when postgis absent | else-branch warning added |
| L5 | `IMAGE_TAG_Q` comment mandated "use instead of raw" while build/push/load/echoes interpolated raw `$(IMAGE_TAG)` in double quotes (injection-shaped) | All images.mk/deploy.mk interpolations switched to break-out-of-quotes Q composition; comment documents the pattern |
| L6 | infra-lint-node would break spuriously if last `.mjs` disappeared (literal glob passed to node) | Empty-list early exit; loop iterates make-resolved files |

Bonus defects found while fixing M3/L5 (all fixed):

| Defect | Fix |
|--------|-----|
| `cytopia/yamllint:1.36.0` **does not exist upstream** (repo publishes no version tags — only `1`/`latest`/alpine variants): the yaml docker fallback could never have worked as pinned | Pinned `cytopia/yamllint:1@sha256:596f…` (yamllint 1.32.0), verified running |
| ALL shellcheck/hadolint/yamllint docker fallbacks were silently broken: globs like `/mnt/**/*.sh` expand against the HOST filesystem (no such dir) and reached containers literally; none of these tools glob internally | File lists resolved by make `$(wildcard)` at parse time, `/mnt/`-prefixed via `$(patsubst)`, shared between local and container branches |
| The hadolint config bind-mount (`/home/hadolint/.hadolint.yaml`) never took effect — config was silently ignored in the fallback | Explicit `--config /cfg/hadolint.yaml` (loading proven with an `ignored:` test config) |

Note for future tool-image bumps: resolve the new digest with
`docker buildx imagetools inspect <repo>:<tag>` and update the var in one
place; `cytopia/yamllint` has no version tags, so re-check behavior when
re-pinning its major-tag digest.

## docs/

Gates at completion: guard proven in a disposable Postgres container
(pinned project image, schema from create_nars_db.sql): fresh seed inserts
58/557/1541 rows · idempotent re-seed without app data succeeds · re-seed
with 1 user + 1 ai_draft_feature aborts with the new ERROR and all data
intact afterwards · pdflatex builds clean (2 passes, no errors, warnings
reduced to the benign release-date notice) · regenerated PDF spot-checked
to contain both new endpoint rows.

| ID | Finding | Fix |
|----|---------|-----|
| D1 | **seed_reference_data.sql destroyed live databases by design**: `TRUNCATE … CASCADE` follows the FK graph (users → refresh_tokens, roads + subtables, inspections, ai_draft_features…), so running the "idempotent re-seed" on any populated DB wiped all application data while its comments advertised safety | Dependent-data guard added: aborts (`RAISE EXCEPTION` + backup hint) when `users` or `ai_draft_features` are non-empty; `communes_boundaries` truncated explicitly as the only legitimate derived dependent |
| D2 | tex REST table claimed to enumerate "the complete REST surface" but omitted `GET /api/features/by-layer/{layer}` and `PUT /api/user/profile` | Both rows added; full table re-verified against every controller route and rate-policy name (auth/api/clear/scattered/logs all match `RateLimitPolicies.cs`) |
| D3 | CONTRIBUTING.md quickstart was broken end-to-end: `POSTGRES_DB=nars` / `-d nars`, but the schema script does `\c nars_db` | Corrected to `nars_db` in all three places |
| D4 | `nars_documentation.pdf` stale — built Aug 16, source edited Aug 20 (campaign changes undocumented in shipped PDF) | Regenerated via pdflatex after D2/D5 |
| D5 | Literal `°` inside math mode (`$\leq 90°$`) → `\textdegree invalid in math mode` warnings on every build | Replaced with `90^{\circ}` |

Verified clean during this pass:

- `boundary-debug` deliberately absent from the API table — correct, the
  controller gates it behind `IsDevelopment()` (LocationsController.cs).
- README.md project table links all resolve (the three TODO.md files exist;
  `nars-tests` row correctly shows `—`).
- uml/*.md Mermaid fences balanced; `nars-uml-diagrams.pdf` is newer than
  all four diagram sources.
- No stale campaign markers (`nars-cluster`, `/api/health`, `memBallast`,
  SSH.NET, unpinned yamllint tag) anywhere under docs/.
- Remaining cosmetic only: ~28 pre-existing Overfull hbox notices in the
  LaTeX build (tight tables/TikZ nodes), no action taken.
