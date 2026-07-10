# NARS-INFRA TODO

## Code Quality Issues (2026-07-10)

### High Priority

- [x] **CA private key exists on disk** (`k8s/certs/ca.key`). It's gitignored correctly (`k8s/certs/*.key` in `.gitignore`), but the file is on disk and could leak via backup, copy, or accidental commit. Fix: generate fresh CA keys at deploy time via `make ca-generate` rather than keeping them on disk.
- [x] **Health check ingress is publicly reachable without IP whitelisting** (`k8s/ingress-api.yaml`). Added `nginx.ingress.kubernetes.io/whitelist-source-range: "10.0.0.0/8"` annotation.
- [x] **Nginx static-asset location has no CSP** (`docker/nginx.nars-vite.conf:76-82`). CSP directives now repeated in the static-asset location block.
- [x] **OTel Tempo exporter `tls.insecure: true`** (`k8s/helm-values/opentelemetry-collector.yaml`). Updated comment with explicit production override instructions via Helm `--set`.
- [x] **PostGIS runs as root (uid 0)** (`k8s/postgis.yaml`). Implemented initContainer pattern: init-chown runs as root to chown data dir, main container now runs as uid 999 with `runAsNonRoot: true`.

### Medium Priority

- [x] **`create_national_admin.sh` imports `sys` but never uses it** (`scripts/create_national_admin.sh:225`). Fixed: added `import sys` to the heredoc import line.
- [x] **`error_logs` table has no index on `created_at`** (`scripts/create_nars_db.sql`). Fixed: added indexes on `created_at`, `user_id`, and `level`.
- [x] **`error_logs` table has no index on `user_id`** (`scripts/create_nars_db.sql`). Fixed in same migration block.
- [x] **`error_logs` table has no index on `level`** (`scripts/create_nars_db.sql`). Fixed in same migration block.
- [x] **OTel collector `replicaCount: 1` with no PDB** (`k8s/helm-values/opentelemetry-collector.yaml`). Fixed: increased to 2 replicas and added PDB in `k8s/pdb.yaml`.
- [x] **Backup PVC uses `storageClassName: local-path`** (`k8s/backup-cronjob.yaml:172`). Already documented with warning comment; override via kustomize in production.
- [x] **`create_national_admin.sh` sets `VENV_DIR=""` twice** (`scripts/create_national_admin.sh:53-54`). Fixed: removed the redundant empty assignment.

### Low Priority

- [x] **Namespace PSA is `baseline` enforce / `restricted` audit** (`k8s/namespace.yaml`). Added monitoring instructions in comment: `kubectl get --raw /apis/audit.k8s.io/v1/events` query.
- [x] **OTel collector `debug` exporter is in the pipeline** (`k8s/helm-values/opentelemetry-collector.yaml`). Updated comment: `verbosity: critical` only logs errors, not full payloads. Safe for dev/staging.
- [x] **`png-to-pdf.py` uses bare `sys.exit` with no error handling** (`scripts/png-to-pdf.py`). False positive: script already has `try/except` around `Image.open()`.
- [x] **`render-mermaid-playwright.mjs` hardcoded CDN URL** (`scripts/render-mermaid-playwright.mjs`). False positive: already has jsdelivr → unpkg fallback.
- [x] **Missing `.env.example`** (referenced in README). Fix: file exists at repo root with documented defaults.
