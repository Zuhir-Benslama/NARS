# nars-infra — Code Quality Review (round 1)

Date: 2026-08-10

## Scope
Reviewed nars-infra (k8s manifests, Dockerfiles, nginx configs, scripts,
migrations, Makefile infra targets) for code quality and hardening.

## Gates verified
- `make infra-lint` (shell, docker, yaml, python, node, makefile, tag-guard) — pass
- `kubectl kustomize nars-infra/k8s/` and `nars-infra/roads/` — pass
- `k8s/certs/ca.crt` tracked is public-only (no private key); `.gitignore`
  covers `ca.key`, `.env`, `docs/pdf/`, `docs/uml/*.{svg,png}`, `__pycache__/`, `.ruff_cache/`
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
- Seed data `docs/seed_reference_data.sql`: 1541 commune rows, no duplicate
  `commune_id` PKs (verified programmatically); all blocks use `COPY FROM stdin`.
- `.gitignore` still covers `ca.key`, `.env`, `docs/pdf/`, `docs/uml/*.{svg,png}`,
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
