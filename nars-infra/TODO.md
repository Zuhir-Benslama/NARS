# nars/nars-infra — Code Quality Fixes

## Done

- [x] **Image digest pinning** — All `FROM` lines pinned to SHA256 digests for reproducibility
- [x] **CA cert gitignore** — Removed `ca.crt` from `.gitignore` (it's already tracked and intentionally committed)
- [x] **PostGIS redundant `|| exit 1`** — Removed from HEALTHCHECK
- [x] **frontend imagePullPolicy** — Changed `Always` → `IfNotPresent` to match API
- [x] **Backup cronjob image** — Switched `postgis/postgis:17-3.5` → `postgres:17-alpine` (~400MB → ~100MB)
- [x] **Frontend ingress HSTS** — Changed `ssl-redirect: "false"` → `"true"` for the main ingress; local dev ingress left as-is
- [x] **Kind-config duplicate IP** — Removed duplicate `127.0.0.1` entry
- [x] **Network policy labels** — Added verification comments for ingress-nginx pod labels
- [x] **Shell trap signals** — Added `INT TERM HUP` to cleanup trap in `create_national_admin.sh`

## Already clean (verified, no action needed)

- **PostGIS runs as root** — Documented as required by the entrypoint (`postgis.yaml:37-43,110-114`). Acceptable with PSA `baseline` profile.
- **PostGIS hostPath volume** — Documented as dev-only with clear warning (`postgis-pv.yaml:13-17`).
- **OTel TLS disabled** — Documented as dev-only with production config instructions (`opentelemetry-collector.yaml:58-80`).
- **`latest` tags for app images** — Documented as dev-only with CI/CD override instructions (`kustomization.yaml:33-47`).
- **PostgreSQL storageClassName** — Documented as dev-only (`backup-cronjob.yaml:13-15`).
- **OpenShift UID compatibility** — Already documented in `frontend-deployment.yaml:45-47`.
- **Mermaid screenshot padding** — Minor; screenshots the SVG element directly.
- **Render-mermaid timeout** — 12s hardcoded; acceptable for the use case.
