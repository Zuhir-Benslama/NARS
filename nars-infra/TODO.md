# NARS Infrastructure — Code Quality Issues

## ✅ Fixed (2026-07-01)

- [x] **🔴 CA private key (`ca.key`) not gitignored** — Added `k8s/certs/ca.key` to `.gitignore`.
- [x] **🟠 Typecheck runs inside Docker build** — Removed `npm run typecheck` from `Dockerfile.nars-vite`.
- [x] **🟠 Backup GPG passphrase via echo+pipe** — Changed to `gpg --passphrase-file <(echo "${GPG_PASSPHRASE}")` in `backup-cronjob.yaml`.
- [x] **🟠 Kind config has placeholder IP** — Changed `<YOUR_HOST_IP>` to `127.0.0.1` in `kind-config.yaml`.
- [x] **🔵 Empty TODO.md** — Populated with this issue tracker.

## 🟠 Major

- [ ] **Backup GPG passphrase via echo+pipe** — Fixed. See above.

## 🔵 Minor

- [ ] **AllowedHosts: "*" in configmap** — `configmap.yaml:18` sets `AllowedHosts: "*"`. Required for K8s probes (pod IP as Host header). Consider pod CIDR scoping if the ingress controller supports `X-Forwarded-Host` validation.

## ✅ Fixed (2026-07-01)

- [x] **💡 Health check ingress may break monitoring** — Removed `ssl-redirect` annotation from `/health` ingress. Health endpoint works over HTTP now.
- [x] **💡 Duplicate connection logic in create_national_admin.sh** — Extracted `connect_db()` into a shared Python module at `${DB_HELPER_DIR}/db_helper.py`; all three heredocs import from it.
- [x] **💡 OpenShift security context for frontend** — Added comment at `frontend-deployment.yaml:44-46` explaining that OpenShift SCC overrides these values.

## Already Documented

- **AllowedHosts: "*" in configmap** — `configmap.yaml:14-17` already has a detailed comment explaining why this is required for K8s probes.
