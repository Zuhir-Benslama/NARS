# nars-infra — Code Quality Review (round 1)

Date: 2026-08-10

## Scope
Reviewed nars-infra (k8s manifests, Dockerfiles, nginx configs, scripts,
migrations, Makefile infra targets) for code quality and hardening.

## Gates verified
- `make infra-lint` (shell, docker, yaml, python, node, makefile, tag-guard) — pass
- `kubectl kustomize nars-infra/k8s/` and `nars-infra/roads/` — pass
- `k8s/certs/ca.crt` tracked is public-only (no private key); `.gitignore`
  covers `ca.key`, `.env`, `../docs/pdf/`, `../docs/uml/*.{svg,png}`, `__pycache__/`, `.ruff_cache/`
- `.ruff_cache/` and `scripts/__pycache__/` exist on disk but are untracked

## Findings & fixes (all applied)

### Medium
1. **`k8s/otel-metrics-service.yaml` selector/labels** — flagged a possible
   selector mismatch (metadata uses `app.kubernetes.io/component: metrics`,
   selector used plain `component:`). Verified against the
   opentelemetry-collector Helm chart (0.159.0) that pods DO carry the plain
   `component: standalone-collector` label, so scraping works. Made the
   selector self-consistent (`app.kubernetes.io/name` + `instance` +
   `app.kubernetes.io/component: standalone-collector`) and documented the
   chart dependency so it stays in sync.
2. **`scripts/create_nars_db.sql` bootstrap drift** — the bootstrap lacked the
   `security_stamp` column (EF migration 20260810062821_AddUserSecurityStamp)
   and the `ai_draft_features` table (nars-infra/migrations/0001...). A fresh
   `make cluster-up` could therefore fail JWT re-validation and the draft
   queue. Fixes:
   - `create_nars_db.sql`: added `users.security_stamp` and the full
     `ai_draft_features` table (constraints, indexes, comment, `DEFAULT
     gen_random_uuid()`).
   - `Makefile postgis-migration-baseline`: now backfills `AddForeignKeys`
     and `AddUserSecurityStamp` when the schema features they produce are
     present (idempotent for partially-migrated clusters).
   - New `make db-migrate-nars` target applies `nars-infra/migrations/*.sql`
     to the deployed DB (idempotent — files use `IF NOT EXISTS`).
   - Validated end-to-end against `postgis/postgis:17-3.5` in a scratch
     container (bootstrap runs clean, geometry CHECK enforced, baseline
     backfills all 5 migration IDs).

### Low
3. `roads/service.yaml` — `app: nars-roads` label → `app.kubernetes.io/name`
   (consistency with the app.kubernetes.io/* convention).
4. `k8s/network-policy.yaml` — removed dead `8888`/`8889` scrape ports from
   `allow-scrape-from-monitoring` (nothing in nars exposes them; only 8080).
5. `roads/deployment.yaml` — set `HOME=/tmp` for nars-roads so torch/GDAL
   user-cache writes are writable under `readOnlyRootFilesystem` (the image's
   `/home/nars-roads` is read-only).

## Verification
- `make infra-lint` — pass
- `kubectl kustomize` (k8s/ and roads/) — pass
- `create_nars_db.sql` — runs cleanly (20 tables / 30 indices); `security_stamp`
  present; `ai_draft_features` inserts pass, mismatched geometry rejected by
  CHECK; `postgis-migration-baseline` backfills all 5 EF migration IDs.

---

# nars-infra — Code Quality Review (round 2)

Date: 2026-08-11

## Scope
Fresh pass over nars-infra (k8s manifests, Dockerfiles, nginx, roads
deployment, scripts, SQL, seed data) beyond the lint gates.

## Gates verified
- `make infra-lint` (shell, docker, yaml, python, node, makefile, tag-guard) — pass
- `kubectl kustomize` (k8s/ and roads/) — pass
- Seed data `../docs/seed_reference_data.sql`: 1541 commune rows, no duplicate
  `commune_id` PKs (verified programmatically); all blocks use `COPY FROM stdin`.
- `.gitignore` still covers `ca.key`, `.env`, `../docs/pdf/`, `../docs/uml/*.{svg,png}`,
  `__pycache__/`, `.ruff_cache/`.
- READMEs (`k8s/README.md`, `docker/README.md`) consistent with manifests.

## Findings & fixes (all applied)

### High
1. **`k8s/backup-cronjob.yaml` — backup encryption always fails.** The
   `nars-backup` image is based on `postgres:17-alpine`, whose postgres user is
   uid **70** (verified: `postgres:x:70:70`); the CronJob ran as `runAsUser:
   999`/`fsGroup: 999`. With no passwd entry for uid 999, HOME falls back to `/`
   and gpg fails:
   `gpg: Fatal: can't create directory '//.gnupg': Permission denied`.
   Reproduced with `docker run --user 999:999 zuhirbenslama/nars-backup:latest
   ... gpg ...`; works with `HOME=/tmp`. Fix: added `HOME=/tmp` env to the
   `pg-dump` container (same pattern as the nars-roads fix, item 5 of round 1).
   Note: if the CronJob is ever re-run with different uid/PVC ownership, keep
   `HOME` pointing at a writable dir.

### Medium
2. **`k8s/network-policy.yaml` — no egress for nars-roads `fetch-weights`
   initContainer.** Under `default-deny-egress`, the roads pod may only reach
   DNS (53) and OTel (4317/4318); the checkpoint download from
   `NARS_ROADS_WEIGHTS_URL` (secret `weights-url`, arbitrary host) is blocked on
   any NetworkPolicy-enforcing CNI (Calico/Cilium). Latent in the kind dev
   cluster because kindnet doesn't enforce NetworkPolicy. Fix: added
   `allow-egress-roads-weights` (nars-roads → any IP on TCP 443/80, commented as
   required for model download).

## Non-findings (checked, no change needed)
- nginx `/login` prefix also matching `/login.html` is correct (routes to the API
  login page).
- Official nginx image symlinks access/error logs to stdout/stderr, so
  `readOnlyRootFilesystem: true` in `frontend-deployment.yaml` is safe.
- `Dockerfile.nars-roads` `COPY weights ./weights` safe — repo contains only
  `.gitkeep`.
- `Dockerfile.nars-postgis` build-context assumption (repo root) documented.
- Secret/ConfigMap key references all consistent across manifests and Makefile.

## Verification
- `make infra-lint` — pass
- `kubectl kustomize` (k8s/ and roads/) — pass
- gpg encryption as uid 999 fails without `HOME=/tmp`, succeeds with it
  (empirically verified against the built `nars-backup` image).

---

# nars-infra — Code Quality Review (round 3)

Date: 2026-08-13

## Scope
Fresh pass over nars-infra (k8s manifests, Dockerfiles, nginx, roads
deployment, scripts, SQL, Makefile guards, helm values) beyond the lint gates.

## Gates verified
- `make infra-lint` (shell, docker, yaml, python, node, makefile, tag-guard,
  local-ingress-guard) — pass
- `kubectl kustomize` (k8s/ and roads/) — pass
- All container limits sum within namespace ResourceQuota (requests.cpu 4 /
  requests.memory 8Gi / limits.cpu 8 / limits.memory 16Gi).
- The initial directory listing from round-2's notes was inaccurate; the real
  k8s/ contents were re-inventoried against `ls` (postgis-pv.yaml exists;
  cert-manager-issuer.yaml and postgis-reference-data-job.yaml do NOT).

## Findings & fixes (all applied)

### High
1. **`k8s/resource-quota.yaml` LimitRange max (2Gi) rejected nars-roads pods.**
   The roads deployment caps memory at 4Gi (PyTorch inference), but the
   namespace LimitRange `max.memory: 2Gi` would fail admission control:
   `exceeds the max limit`. Fix: raised `max.memory` to 4Gi (≥ the largest
   container limit), with a comment explaining the coupling so it can't regress.

### Medium
2. **`app-deployment.yaml` pulled the whole `nars-secrets` via `envFrom`.** The
   API only reads `ConnectionStrings__DefaultConnection`, `Jwt__SecretKey`,
   `AdminSignup__SignupToken`, `Segmentation__InternalToken` (verified against
   Program.cs / SegmentationServiceExtensions.cs). It has no need for
   `postgres_password` or `gpg-passphrase`. Fix: replaced `envFrom secretRef`
   with explicit `secretKeyRef` env entries (least privilege; configmap
   `envFrom` kept).
3. **`Dockerfile.nars-roads` base image not digest-pinned.** It was the only
   Dockerfile using a mutable tag (`python:3.11-slim`); every other base image
   is pinned by `@sha256`. Fix: pinned to the amd64 digest
   `78b39ef14d8e...` (verified via `docker manifest inspect`).
4. **Dev-only local ingresses shipped in the base kustomization.** The base
   (applied by `make kustomize-apply`) contains `nars-api-local` (no mTLS, no
   host restriction) and `nars-frontend-local` (hostless catch-all). A
   production apply would silently expose `/api` and `/login` without client
   certs. Fix: new `_check-local-ingresses` Makefile guard — fails
   `kustomize-apply` when `DEPLOY_ENV != dev` and the local ingresses are in
   the kustomize output; self-tested via `infra-lint-local-ingress-guard`.

### Low
5. `k8s/otel-metrics-service.yaml` — comment said "verified against chart
   0.159.0" but the Makefile pins `OTEL_COLLECTOR_VERSION ?= 0.169.0`. Fix:
   comment now points at the Makefile var and instructs re-verification of the
   selector on chart bumps.

## Non-findings (checked, no change needed)
- `create_national_admin.sh` trap `rm -rf "${DB_HELPER_DIR}" "${VENV_DIR}"`
  with unset VENV_DIR → `rm -rf ""` returns 0 (verified empirically), no bug.
- Backup gpg passphrase passed via `--passphrase` on the command line — visible
  only inside the pod's own process list; acceptable, container is
  single-purpose.
- `allow-egress-roads-weights` grants 443/80 egress to the whole roads pod
  (initContainer needs it; NetworkPolicy is pod-scoped, cannot restrict to the
  init container alone) — documented tradeoff, required for weights download.
- postgis `init-chown` runs as root — required to chown the data dir before the
  main container drops to uid 999; documented in the manifest.
- nginx local-dev ingresses in the same files as prod ingresses is intentional
  (multi-doc YAML); the new guard protects production applies.
- HPA memory metric on nars-api kept (comment already flags its caveat).

## Verification
- `make infra-lint` — pass (including the new local-ingress-guard self-test)
- `kubectl kustomize` (k8s/ and roads/) — pass
- `_check-local-ingresses` rejects in production, passes in dev (verified)
- LimitRange max (4Gi) ≥ every container limit in the namespace (verified by
  grepping all deployment/cronjob limits)

---

# nars-infra — Code Quality Review (round 4)

Date: 2026-08-13

## Scope
Fresh pass over the Makefile (all 1411 lines) and the smoke-test
assumptions against the API implementation.

## Gates verified
- `make infra-lint-makefile` — pass (syntax + undefined-variable dry-run)
- `make infra-lint-tag-guard` — pass (self-tests)
- `make infra-lint-local-ingress-guard` — pass (self-tests)

## Findings & fixes (all applied)

### Medium
1. **`Makefile smoke-test` health body assertion never matched.** The target
   grepped the `/health` response body for a bare `^Healthy$`, but the API
   registers `app.MapHealthChecks("/health")` with the default response writer
   (no custom `HealthCheckResponseWriter` anywhere in nars-api) — it emits JSON
   (`{"status":"Healthy","totalDuration":...,"entries":{...}}`). So a healthy
   cluster always failed the "body unexpected" branch. Fix: match the JSON
   status field instead — `grep -qE '"status"[[:space:]]*:[[:space:]]*"Healthy"'`.
   Verified the pattern accepts the real JSON output and rejects a
   non-Healthy status.

## Non-findings (checked, no change needed)
- `POSTGIS_GET_POD_CMD` label `app.kubernetes.io/name=postgis` matches
  `postgis.yaml` deployment/pod labels.
- `_check-secrets` and the `.env` auto-generation remain consistent (all six
  secrets generated, exported, and validated).
- The remaining `/health` checks are status-code only (no body assertions)
  and are correct.
- `db-restore` FILE validation, `cluster-clean` interactive confirmation, and
  `clean` teardown refusal are all intact (data-safety rules).

---

# nars-infra — Code Quality Review (round 5)

Date: 2026-08-16

## Scope
Fresh pass over nars-infra (Dockerfiles, k8s manifests, helm-values, nginx
configs, scripts, SQL, Makefile infra targets) beyond the lint gates.

## Gates verified
- `make infra-lint` (shell, docker, yaml, python, node, makefile, tag-guard,
  local-ingress-guard) — pass
- `kubectl kustomize` (k8s/ and roads/) — pass (roads resources carry
  `namespace: nars` in the output)
- `helm template` otel-collector chart 0.169.0 + values + `--set image.tag` — pass
- nginx config: `nginx -t` passes (outside-cluster DNS hosts stubbed in test env;
  the cluster-internal `resolver`/upstream names are the only blockers there)
- All `secretKeyRef` keys (nars-secrets × 6, nars-roads-secrets × 2) exist in
  `make secrets-apply` output and in `secret.yaml` reference template
- ConfigMap keys used by `backup-cronjob.yaml` match the ConfigMap
- Images/ports/probes consistent: API `/health` (PipelineExtensions.cs),
  roads `/healthz`, nginx 8080 → `nars-api.nars.svc.cluster.local:8080`,
  probes `Host: nars.dz` (in AllowedHosts), `otel-metrics-service.yaml`
  selector `standalone-collector` + named port `prometheus-exporter` (8889)
  ⇄ ServiceMonitor
- UIDs match images: postgis 999, nginx 101, api 1654 (.NET APP_UID), collector 65534

## Findings & fixes (all applied)

### Low
1. **`k8s/helm-values/opentelemetry-collector.yaml` — collector image tag
   drifted from the chart.** Chart 0.169.0 has `appVersion: 0.158.0` (verified
   via `helm show chart`), while the values file pinned `image.tag: 0.120.0`
   with no link between them. Fix: new `OTEL_COLLECTOR_IMAGE_TAG ?= 0.120.0`
   in the Makefile; `observability-otel-collector` now passes
   `--set image.tag="$(OTEL_COLLECTOR_IMAGE_TAG)"`; values file comment
   documents the chart-vs-image mismatch so the two can't silently drift.
2. **`k8s/README.md` manual-secrets example was incomplete.** It listed only
   `postgres_password` / `ConnectionStrings__DefaultConnection` /
   `Jwt__SecretKey`, but the API and backup CronJob fail to start without
   `gpg-passphrase`, `AdminSignup__SignupToken`, and `Segmentation__InternalToken`
   too. Fix: all six keys documented, plus the `nars-roads-secrets` pair, with a
   note preferring `make secrets-apply` (avoids secrets in shell history).
3. **`roads/{deployment,service}.yaml` had no `namespace:`.** Works only via the
   parent kustomization; `kubectl apply -f nars-infra/roads/` landed resources in
   the default namespace. Fix: explicit `namespace: nars` (matches parent, so
   standalone apply is safe).
4. **`docker/nginx.nars-vite.conf` duplicated the CSP build.** Two locations
   each built the same 8-line CSP via `set`; risk of drift and the static-asset
   block lacked `Permissions-Policy` (`add_header` doesn't inherit). Fix: built
   `$csp` once at server level (variables DO inherit into locations); both
   locations just `add_header Content-Security-Policy $csp always`; static
   block now also sets `Permissions-Policy`.
5. **`scripts/render-mermaid-playwright.mjs` — broken single-file CLI.** Passing
   `node script.mjs <file.md>` treated the file as a directory (arg parsing
   expected `inputDir outputDir files...`). Fix: `.md` arg now enters a
   single-file mode (`dirname`/`basename`); added an `existsSync` guard with a
   friendly skip; replaced deprecated `page.waitForTimeout(500)` with a
   `setTimeout` promise; removed unused `writeFileSync` import.
6. **`k8s/backup-cronjob.yaml` — no `timeZone` on the schedule.** `0 2 * * *`
   ran at 02:00 in the kube-controller-manager's local timezone. Fix:
   `timeZone: "Africa/Algiers"` (UTC+1, no DST; requires K8s >= 1.27).

## Non-findings (checked, no change needed)
- nginx `resolver kube-dns...` and static `proxy_pass` FQDNs fail `nginx -t`
  outside the cluster by design (hosts resolve via kube-dns in-cluster) — not
  a config error; verified the config syntax by stubbing the hosts.
- postgis `init-chown` as root with `allowPrivilegeEscalation: true` is required
  and documented.
- Backup `runAsUser: 999` vs alpine postgres uid 70 is fine (pg_dump connects
  over TCP; `HOME=/tmp` fixes gpg) — documented in the manifest.
- `render-mermaid-playwright.mjs` default file list matches `docs/uml/*.md`.
- CSP stays effective with the server-level variable (both SPA and static
  locations emit the header; verified identical value in both blocks).

## Verification
- `make infra-lint` — pass
- `kubectl kustomize` (k8s/ and roads/) — pass; roads Deployment/Service show
  `namespace: nars` once each
- `node --check` on the mermaid script — pass
- `helm template` otel-collector 0.169.0 with the values + `--set image.tag=0.120.0`
  renders `otel/opentelemetry-collector-contrib:0.120.0`
- `nginx -t` on the refactored config (cluster DNS stubbed) — syntax ok

# nars-infra — Makefile + make/ review (round 6)

Date: 2026-08-16

## Scope
Fresh review of the build system: root `Makefile` and all `make/*.mk`
(cluster, db, deploy, images, observability, proxy, quality, smoke, tests).
Verified: `infra-lint-makefile` passes, `make -n cluster-up` is a true dry-run
(SUBMAKE indirection), fresh-clone/undefined-var check passes, guard
self-tests pass, coverage-threshold doc accurate vs the nars-tests csproj.

## Findings & fixes (all applied)

### Medium
1. **IMAGE_TAG reaches shell recipes unvalidated — confirmed code execution.**
   `_check-pinned-tag`, `_warn-latest-tag` and `kustomize-apply`'s
   `awk -v tag="$(IMAGE_TAG)"` interpolated the raw tag into double-quoted
   shell contexts; a value like `` `touch /tmp/x` `` executed the backtick
   (reproduced before the fix). `$(...)` tags were neutralized by make itself,
   but backticks/`"` passed through. Fix:
   - `IMAGE_TAG_Q = '$(subst ','"'"',$(IMAGE_TAG))'` (single-quote-escaped form)
     used wherever the tag lands in a shell context (grep checks, `awk -v`,
     `kustomize edit set image`).
   - Shared `_check_tag_cmd` charset guard (`[a-zA-Z0-9._-]`) run first in
     `_warn-latest-tag` / `_check-pinned-tag`; error messages never put the
     value inside double quotes (a hostile value must not be echoable).
   - `_build-*` targets and `frontend-update` now require `_warn-latest-tag`,
     so `docker build`/`kind load` tag interpolation is always pre-validated.
   - New "hostile tag rejected" case in `infra-lint-tag-guard` self-test.
   Post-fix re-test: `IMAGE_TAG='\`touch /tmp/pwn2\`'` is rejected, no file
   created, message shows the value literally.
2. **Fresh `make .env` omitted `NARS_ROADS_WEIGHTS_URL`** even though
   `.env.example` documents it, so a fresh `cluster-up` deployed a roads pod
   whose fetch-weights initContainer could never become ready. Fix: the `.env`
   target now emits the documented default URL; `_check-secrets` now fails fast
   if it is missing.

### Low
3. **`observability-port-forward` reported success without verifying the
   forwards** — a dead kubectl/stack yielded "✓ Port-forwards running" with
   empty logs. Fix: after launch, poll each log for kubectl's
   `Forwarding from` marker and fail if any forward did not start.
4. **`db-migrate-nars` printed "✓ applied" for an empty migrations dir** — all
   files skipped silently. Fix: count applied files and fail if none found.
5. **`_check-local-ingresses` silently passed when kubectl was missing**
   (`2>/dev/null` + grep found nothing → green). Fix: capture kustomize output,
   fail on failure, and only run the check outside dev (dev skips entirely —
   verified with `KUBECTL=no-such-kubectl`).

## Non-findings (checked, no change needed)
- `postgis-password-sync` uses `$$POSTGRES_PASSWORD` (shell env) to dodge SQL
  quoting for generated (base64) secrets — documented at the recipe.
- `apply -f - < file` stdin redirects in observability.mk are redundant but
  harmless; `kubectl apply -f file` also accepts a file.
- `make -n` regenerates a missing `.env` before the dry-run (GNU make
  auto-remake) — accepted behavior; the generated secrets are fresh each time.

## Verification
- Injection re-test (`\`touch /tmp/pwn2\``, `x;touch /tmp/pwn4`,
  `$(touch ...)`) — all rejected, none executed
- `make infra-lint-tag-guard` (incl. new hostile-tag case) — pass
- `make infra-lint-local-ingress-guard` — pass
- `make infra-lint-makefile` — pass; `make help` — renders
- `make -n cluster-up` — true dry-run (exit 0, no side effects)
- Fresh `.env` in a scratch dir contains `NARS_ROADS_WEIGHTS_URL`
- `_check-secrets` with empty weights URL — fails with message
- `awk -v tag` renders single-quoted (`-v tag='abc123'`) in `make -n`
  `kustomize-apply`

---

# nars-infra — Docs review (round 7)

Date: 2026-08-16

## Scope
Cross-checked `docs/nars_documentation.tex` and `docs/seed_reference_data.sql`
against the API/web implementation. Every normative claim was verified against
code; only genuine mismatches were reported, then fixed. `docs/uml/*.md` were
also read (no changes applied — see Non-findings).

## Gates verified
- Claims verified against source: Θ_max=90° (`RoadTurnAngleDegrees` 90.0),
  δ_road=20 m (`RoadConnectivityMeters` 20.0), coverage buffer 10 m
  (`DistrictBoundaryToleranceMeters` 10.0), δ_J=30 m
  (`nars-web/src/map/roads/road-graph.ts` `CONNECT_M = 30`), snaps
  40/40/20/20 px (`nars-web/src/config/index.ts`), R=6,371,000
  (`config/index.ts:159`), payload 512 KiB (`MaxFeatureDataSize` 524288),
  coords 10,000 (`MaxCoordinateCount`), bcrypt cost 11 (BCrypt.Net-Next
  default), refresh token 64-byte/SHA-256/7 days, auth 5/30s/3 + api 60/1min/6,
  `Guid.CreateVersion7()` in `FeaturesController.cs:68`.
- JWT claims (`Services/JwtService.cs:37-60`): user_id, username, name, email,
  role, security_stamp always; commune_id/daira_id/wilaya_id conditional.
  `appsettings.json`: `Jwt:ExpiresInMinutes 60`, `RefreshExpiresInDays 7`.
- Rate policies: auth 5/30s/3, clear 3/10min/1, api 60/1min/6, scattered
  5/5min/1, logs 30/1min/1 (appsettings only carries auth/clear/api;
  scattered/logs defaults in `Infrastructure/RateLimitOptions.cs`).
- Pagination: `Infrastructure/Pagination.cs` `MaxTake = 500`.
- Seed SQL: clean run 58 wilayas / 557 dairas / 1541 communes.

## Findings & fixes (all applied)

### Medium
1. **`nars_documentation.tex` JWT lifetime claimed 24 h.** `appsettings.json`
   sets `Jwt:ExpiresInMinutes = 60`; the doc said `T_access = 1,440 min =
   24 h`. Fix: 60 min / 1 h.
2. **`seed_reference_data.sql` truncated tables outside the transaction.**
   The idempotency `DO` block (`TRUNCATE ... CASCADE`) ran before `BEGIN`,
   so a mid-COPY failure left all tables empty (reproduced: second scratch
   DB → 0/0/0). Fix: moved the block inside the transaction (after `BEGIN`).
   Post-fix atomicity test (injected FK-violating commune row mid-COPY):
   data intact at 58/557/1541.

### Low
3. **Stack versions stale in tex** (table + architecture figure):
   PostgreSQL 15→17, EF Core 9.x→10.x, Geoman 0.7.x→0.8.x.
4. **`eq:jwt_claims` claims set incomplete.** Added `role`/`security_stamp`
   (always) and `daira_id`/`wilaya_id` (conditional) with a note on why
   `security_stamp` exists.
5. **Rate-limiting section said "Two policies".** There are five named
   policies; rewrote intro and added `scattered` + `logs` rows to
   `tab:rate_limits`; `clear` row now points at `POST /api/features/clear`.
6. **Client-side validation section described checks that no longer exist**
   (Minimum Road Length `L_min = 10 m`, Polygon Ring Closure). Code uses
   geometry cardinality (LineString ≥2, Polygon ≥3) and city-centre radius
   bounds (5 m–50,000 m) in `draw-save.ts::validateGeometry`; rewrote the
   section, the equation labels, and the validation-flow figure nodes/caption.
7. **REST endpoint table was stale.** `/api/save`, `/api/load`,
   `/api/update/:id`, `/api/delete/:id`, `/api/clear`, `/api/stats`,
   `/api/locations`, `/api/spatial/commune-boundary`, `/api/scattered-status`
   replaced with the real routes; added `feature-types`, `road-side`,
   administrative reference, Administration, Field inspection, and AI draft
   features sections; auxiliary `refresh-scattered` + `logs` rows added;
   `Pagination.MaxTake` 2,000→500; remaining `/api/save`→`/api/features` and
   `/api/load`→`/api/features` references fixed.
8. **`seed_reference_data.sql` never analyzed `communes`.** Only
   `wilayas`/`dairas` were VACUUMed. Fix: added `VACUUM ANALYZE
   public.communes;`.
9. **Commune 1264 `Tamellahet` row was malformed.** Fix: name → `تملاحت،
   دائرة لرجام، تسمسيلت، الجزائر`; coordinates → daira 448 (لرجام)
   `35.760659, 1.4502798` as a bounded approximation (authoritative commune
   coords unverifiable — Wikidata has no entry; search inconclusive).

## Non-findings (checked, no change needed)
- `docs/pdf/*` (gitignored) are older than the sources — build artifact, not
  a repo defect.
- No duplicate `commune_id` PKs; all 1,543 commune rows carry coordinates.

## Follow-up (round 7.1)
- `docs/uml/nars-sequence-diagram.md` and `nars-vite-sequence-diagram.md`
  referenced stale routes (`POST /api/save`, `GET /api/load`,
  `PUT /api/update/{id}`, `DELETE /api/delete/{id}`). Fixed to the real
  `FeaturesController` routes (`/api/features`, `/api/features/{id}`) — 8
  occurrences. All remaining `/api/*` references in `docs/uml/` now match
  the API (`/api/road-side`, `/api/current_user`, `/api/signin`,
  `/api/refresh`, `/api/validate/*`, `/api/admin/authorized-signup`).
  Verified: all four diagrams render via
  `render-mermaid-playwright.mjs` (playwright 1.59.0 matching the cached
  firefox-1511) with zero mermaid syntax errors.

## Verification
- SQL in a throwaway `postgis/postgis:17-3.5-alpine` container (no volume,
  cleaned up): clean run 58/557/1541 with 3× VACUUM; idempotent re-run OK
  (`NOTICE: Seed data already present — truncating and re-seeding.`); atomicity
  proven both ways (old structure → 0/0/0 on failure; new structure → intact);
  Tamellahet row verified in DB.
- `xelatex` (3 passes, `-halt-on-error`) — **pass** (27 pages, 0 errors,
  0 unresolved references). The four overfull hboxes introduced by the edits
  (JWT claims equation, `Jwt:ExpiresInMinutes` token, `tab:rate_limits`
  growth, validation text) were all fixed; remaining 26 overfull/underfull
  warnings and the `—`/`°` missing-character warnings are pre-existing
  (identical in a pristine-HEAD build; the `°` in the validation figure's
  `Angle $\leq 90°$` node predates this round). The initial "compile fails"
  finding was an environment gap: `texlive-enumitem` and
  `texlive-algorithmicx` (`algpseudocode.sty`) were missing from the system
  TeXLive; after `sudo dnf install -y texlive-enumitem texlive-algorithmicx`
  the genuine build succeeds without stubs.
