# nars-infra — Code Quality

All 15 issues fixed. Linters: Hadolint ✅ ShellCheck ✅ Yamllint ✅

## Fixed

### Critical
- [x] C1 — Network policy now allows `postgis-backup` ingress to PostGIS (port 5432)
- [x] C2 — Documented root user security trade-off with production mitigation steps
- [x] C3 — Makefile `secrets-apply` uses temp files instead of `--from-literal` (no `ps aux` leak)

### Major
- [x] M1 — `imagePullSecrets: [{name: regcred}]` added to all three Deployments
- [x] M2 — OTel debug exporter verbosity set to `critical` (errors only)
- [x] M3 — Added production note to enable Loki auth
- [x] M4 — Added production note to enable Tempo/OTel TLS
- [x] M5 — Added `--no-owner` restore implications comment
- [x] M6 — Backup image pinned to `postgres:17-alpine@sha256:af194cc...`

### Minor
- [x] m1 — `mktemp` exit code masking fixed (separate declaration + assignment)
- [x] m2 — `png-to-pdf.py`: corrupt PNG handling with try/except
- [x] m3 — PostGIS Dockerfile: added build context requirement comment
- [x] m4 — Health ingress: added `whitelist-source-range` recommendation
- [x] m5 — `kustomization.yaml`: added overlay structure documentation
- [x] m6 — Seed SQL: added `VACUUM ANALYZE` after bulk load
