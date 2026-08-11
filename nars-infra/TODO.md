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
