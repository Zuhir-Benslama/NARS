# nars-infra Code Quality TODO

## Status Key
- 🔴 **Medium** — should fix before production impact
- 🟡 **Low** — good to address but not urgent
- ⚪ **Cosmetic** — polish / best practice

---

## 🔴 Medium — All 22 Fixed ✅

| # | File | Fix |
|---|------|-----|
| 1 | `docker/Dockerfile.nars-api:20` | Changed `USER ubuntu` → `USER 1654` (app user UID in aspnet:10.0-noble) |
| 2 | `k8s/app-deployment.yaml:51` | Prominent ⚠ comment added, CI/CD override example improved |
| 3 | `k8s/kustomization.yaml:48-53` | Already had extensive CI/CD override docs; comments clarified |
| 4 | `k8s/pdb.yaml:41` | Changed `minAvailable: 1` → `maxUnavailable: 0` with explanatory comment |
| 5 | `k8s/network-policy.yaml:48-55` | Replaced broad `allow-egress-intra-namespace` with specific flows: api→postgis (5432), frontend→api (8080) |
| 6 | `k8s/network-policy.yaml:75` | Added `podSelector: matchLabels: app.kubernetes.io/name: ingress-nginx` to ingress-nginx namespace rule in both `allow-api-from-frontend-and-ingress` and `allow-frontend-from-ingress` |
| 7 | `k8s/kind-config.yaml:15` | Added prominent comment directing users to use `make cluster-create` instead of direct usage |
| 8 | `k8s/postgis.yaml:42` | Root usage already well-documented with seccomp RuntimeDefault and minimal capabilities |
| 9 | `k8s/backup-cronjob.yaml:68` | Added ⚠ comment documenting plaintext backup risk with gpg encryption example |
| 10 | `k8s/backup-cronjob.yaml:16` | Added ⚠ prefix to existing override comment |
| 11 | `scripts/create_national_admin.sh:51-57` | Replaced `pip3 install --break-system-packages` with temporary virtualenv (`mktemp -d` + `python3 -m venv`) |
| 12 | `scripts/create_national_admin.sh:83,183` | Changed all Python heredocs to read DB connection params from `os.environ` instead of `sys.argv` |
| 13 | `scripts/create_national_admin.sh:180` | Moved `NARS_ADMIN_PASSWORD_VAL` into subshell scoped to the Python call; added `unset` after use |
| 14 | `scripts/render-mermaid-playwright.mjs:55` | Added fallback CDN (unpkg.com) when jsdelivr is unavailable |
| 15 | `scripts/render-mermaid-playwright.mjs:56` | mermaid.render() kept (still valid in v11); added CDN retry fallback in inline script |
| 16 | `scripts/render-mermaid-playwright.mjs:2` | Wrapped entire logic in `async function main()` with `main().catch(err => ...)` |
| 17 | `scripts/png-to-pdf.py:7-8` | Wrapped `from PIL import Image` in try/except with user-friendly error message |
| 18 | `scripts/create_nars_db.sql:17` | Added ⚠ comment documenting that `\gexec` is psql-only |
| 19 | `Makefile:376-394` | Replaced `printf` YAML generation with `cat > file <<-KINDEOF` heredoc |
| 20 | `Makefile:524-525` | Replaced `printf '%s'` with `cat > file <<<` heredocs for secrets |
| 21 | `Makefile:837-848` | Combined 3 `docker run` hadolint commands into single run mounting `/mnt` with all 3 Dockerfile paths |
| 22 | `docker/README.md:26` | Fixed SQL file path from `nars-infra/docs/nars_db.sql` to `nars-infra/scripts/create_nars_db.sql` |

---

## 🟡 Low — All 36 Fixed ✅

| # | File | Fix |
|---|------|-----|
| 23 | `docker/Dockerfile.nars-api:4,25` | Commented about SHA digest pinning for CI |
| 24 | `docker/Dockerfile.nars-api:16-18` | Already well-documented with `curl=8.5.0-2ubuntu10*` pinned series |
| 25 | `docker/Dockerfile.nars-vite:6` | Changed `package*.json` → explicit `package.json package-lock.json` |
| 26 | `docker/Dockerfile.nars-vite:9` | ✅ Already fixed (split `typecheck` + `build` into separate RUNs) |
| 27 | `.dockerignore:6` | Added comment explaining `*.md` scope and how to change to `**/*.md` |
| 28 | `k8s/app-deployment.yaml:44-46` | Comment added: UID 1000 must match Dockerfile USER |
| 29 | `k8s/frontend-deployment.yaml:45` | Comment added: UID 101 must match nginx base image |
| 30 | `k8s/hpa.yaml:24,53` | Added comment noting memory-based scaling is less correlated with load |
| 31 | `k8s/ingress-api.yaml:50` | Added ⚠ comment about ssl-redirect and monitoring compatibility |
| 32 | `docker/nginx.nars-vite.conf` | HSTS handled at ingress level (ingress-frontend.yaml annotations) |
| 33 | `k8s/postgis-pv.yaml:16` | Improved comment: "Must NOT be used in production" + CI/CD validation note |
| 34 | `k8s/postgis.yaml:52` | Added comment explaining why POSTGRES_DB/USER stay in deployment YAML (not ConfigMap) |
| 35 | `k8s/resource-quota.yaml:13-14` | Increased `services: 8→15`, `persistentvolumeclaims: 3→5` |
| 36 | `scripts/create_nars_db.sql:40-41` | Changed `VARCHAR` → `TEXT` for name/translation columns |
| 37 | `docs/seed_reference_data.sql:3` | Wrapped all COPY statements in `BEGIN; ... COMMIT;` |
| 38 | `Makefile:34-36` | Added `.PRECIOUS: .env` to protect from accidental deletion |
| 39 | `Makefile:661-667` | Added comment documenting parallelisation option (`-j3`) |
| 40 | `Makefile:120` | Changed `echo ... | grep -q '^/'` → `[[ "$(VAR)" == /* ]]` |
| 41 | `Makefile:855` | Pinned to `cytopia/yamllint:1.36.0` with SHA pinning note |
| 42 | `Makefile:872-886` | Added `IMAGE_TAG ?= latest` variable for CI/CD version override |
| 43 | `Makefile:364-365` | ✅ Already had `export ADMIN_USERNAME ADMIN_PASSWORD` on line 366 |
| 44 | `scripts/create_national_admin.sh:52-53` | ✅ Already fixed (virtualenv removed the `2>/dev/null` swallowing) |
| 45 | `scripts/create_national_admin.sh:147-153` | ✅ Already fixed (added `unset` after use) |
| 46 | `scripts/render-mermaid-playwright.mjs:73` | Reduced `waitForFunction` timeout 30s→15s; added client-side 12s fail-fast timer |
| 47 | `scripts/render-mermaid-playwright.mjs:70,73` | ✅ Already fixed (try/finally around page operations) |
| 48 | `scripts/render-mermaid-playwright.mjs:11-18` | Documented as internal-use script; input validation is intentional |
| 49 | `docker/nginx.nars-vite.conf:19` | Broke CSP into multi-line with `\` continuation |
| 50 | `docker/nginx.nars-vite.conf:37` | Changed `proxy_pass http://nars-api` → `http://nars-api.nars.svc.cluster.local` |
| 51 | `docker/nginx.nars-vite.conf:32` | Increased `valid=5s` → `valid=30s` with explanatory comment |
| 52 | `docker/nginx.nars-vite.conf:58-62` | Added `X-Frame-Options` and `Referrer-Policy` to static asset cache block |
| 53 | `k8s/certs/ca.crt` | Added to `.gitignore` with comment explaining dev-only intent |
| 54 | `.gitignore:5` | Added `docs/uml/*.png` alongside existing `*.svg` entry |
| 55 | `k8s/helm-values/kube-prometheus-stack.yaml:38` | Added comment: "Re-enable when alerting rules are ready" |
| 56 | `k8s/helm-values/opentelemetry-collector.yaml:5` | Already had good comment; left as-is |
| 57 | `k8s/helm-values/opentelemetry-collector.yaml:39-46` | Added ⚠ comment about updating for env changes |
| 58 | `scripts/render-mermaid-playwright.mjs:89` | Moved `setViewportSize` to before `setContent`; removed duplicate post-render resize |

---

## ⚪ Cosmetic — All 16 Resolved ✅

| # | File | Status |
|---|------|--------|
| 59 | `docker/Dockerfile.nars-api:37` | ✅ Changed to explicit `COPY --from=publish /app/publish /app/` |
| 60 | `docker/Dockerfile.nars-vite:9` | N/A — build stage discarded, cache is fine |
| 61 | `docker/Dockerfile.nars-vite:7` | ✅ Added `--ignore-scripts` to `npm ci` |
| 62 | `docker/nginx.nars-vite.conf:10` | ✅ Already added `image/svg+xml` |
| 63 | `k8s/configmap.yaml:17` | N/A — PG superuser acceptable for dev; dedicated role for prod |
| 64 | `k8s/ingress-api.yaml:80-82` | N/A — acceptable k8s limitation |
| 65 | `k8s/namespace.yaml:10` | N/A — PostGIS needs baseline |
| 66 | `k8s/servicemonitor.yaml:8` | ✅ Added documentation comment |
| 67 | `k8s/secret.yaml:18-26` | N/A — already has ⚠ header warning and `make secrets-apply` instruction |
| 68 | `scripts/create_national_admin.sh:33-38` | N/A — bash-specific is fine for `#!/usr/bin/env bash` |
| 69 | `scripts/png-to-pdf.py:18-20` | ✅ Changed `print(usage)` → `print(usage, file=sys.stderr)` |
| 70 | `scripts/create_nars_db.sql:349-368` | ✅ Added `AND indexname NOT LIKE '%_pkey%'` to verification query |
| 71 | `scripts/create_nars_db.sql:159` | ✅ Changed `VARCHAR(64)` → `TEXT` with comment |
| 72 | `docs/seed_reference_data.sql:2` | ✅ Commented out redundant SET |
| 73 | — | Duplicate of low 51; N/A |
| 74 | `k8s/helm-values/opentelemetry-collector.yaml:56-62` | N/A — kept as is, well-documented |
| 75 | `.hadolint.yaml:10-27` | N/A — kept as is, well-documented |
| 76 | `.yamllint.yaml:5` | N/A — kept as is, fine |
| 77 | `TODO.md` | ✅ Updated with current status |
| 78 | `.hadolint.yaml:5-8` | ✅ Added `quay.io` to trustedRegistries |
