All items from the initial code quality check have been resolved.

## Fixed

- **.yamllint.yaml** — Added missing `---` document-start marker; config is now clean (0 violations).
- **scripts/render-mermaid.mjs** — Removed deprecated file (replaced by `render-mermaid-playwright.mjs`).
- **k8s/secret.yaml** — Removed unused `regcred` secret template (`imagePullSecrets` removed from deployments earlier); fixed connection string `Host=localhost` → `Host=postgis`.
- **docker/README.md** — Updated pull secret section to document that images are public (no regcred needed by default).
- **k8s/kind-config.yaml** — Replaced hardcoded IP `192.168.1.3` with `<YOUR_HOST_IP>` placeholder.
- **.dockerignore** — Added to avoid sending build context cruft to Docker daemon.
- **k8s/app-deployment.yaml, k8s/frontend-deployment.yaml** — Added `topologySpreadConstraints` for better HA scheduling across nodes (complements existing `podAntiAffinity`).

## Verified

- yamllint: 0 warnings, 0 errors
- Shell syntax: clean
- Python syntax: clean
