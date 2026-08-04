# Code Quality Issues — nars-infra

Findings from the Aug 3 infrastructure review. Verified clean: hadolint (4 Dockerfiles),
yamllint (custom `.yamllint.yaml`), shellcheck, ruff check + format, `node --check`,
`kubectl kustomize build` (42 resources). No secrets/keys/bytecode tracked; `secret.yaml` is a
doc-only reference template; pod hardening is consistent (runAsNonRoot, `drop: [ALL]`,
seccomp RuntimeDefault, `automountServiceAccountToken: false`).

## Low

- [x] **L1 — Python helper script is not linted in CI**
  `nars-infra/scripts/png-to-pdf.py` — ruff passes locally (there is even a `.ruff_cache/`), but
  `make infra-lint` (Makefile:1073-1078) only runs shell/docker/yaml/makefile targets and the CI
  `infra-lint` job (`.github/workflows/ci.yml:45-70`) has no ruff step, so the script can rot
  undetected. The `render-mermaid-playwright.mjs` script has no linter either (only `node --check`).
  DONE: added `infra-lint-python` (`ruff check` + `ruff format --check` on `nars-infra/scripts/`,
  docker fallback `ghcr.io/astral-sh/ruff:0.15.15`) and `infra-lint-node` (`node --check` on
  `render-mermaid-playwright.mjs`, fallback `node:22-alpine`); both wired into `infra-lint` and the
  CI job. `RUFF_IMAGE`/`NODE_IMAGE` overridable in Makefile.

- [x] **L2 — `allow-scrape-from-monitoring` NetworkPolicy is broader than needed**
  `nars-infra/k8s/network-policy.yaml:232-252` — `podSelector: {}` grants the monitoring namespace
  ingress to *every* pod in the `nars` namespace on ports 8080/8888/8889. Harmless today (only
  `nars-api` listens on 8080) but it grants permissions to pods that have no metrics endpoint.
  DONE: `podSelector` narrowed to `app.kubernetes.io/name: nars-api` +
  `app.kubernetes.io/component: api`; comment updated. Default-deny posture preserved.

- [x] **L3 — `latest` image tags are the manifest default**
  `nars-infra/k8s/kustomization.yaml:71-78` (and `image: zuhirbenslama/*:latest` in
  `app-deployment.yaml:59`, `frontend-deployment.yaml:60`, `postgis.yaml:51`, `backup-cronjob.yaml:65`)
  — documented as dev-only and guarded by `make kustomize-apply` for `DEPLOY_ENV != dev`
  (Makefile `_check-pinned-tag`), but there is no CI assertion that fails a production apply
  if the tag is still `latest`.
  DONE: added `infra-lint-tag-guard` self-test target — asserts `_check-pinned-tag` (1) rejects
  `IMAGE_TAG=latest` under `DEPLOY_ENV=production`, (2) accepts a pinned tag, (3) honors the
  `ALLOW_LATEST=1` emergency override. Wired into `infra-lint` and the CI job, so a regression
  in the guard (e.g. someone widens it) fails CI.

## Clean (verified, no action)

- No secrets in any manifest: `secret.yaml` has no `stringData`; `ca.crt` (public) is committed but
  `ca.key` is gitignored; no `.env`/`.pyc`/`.key` tracked.
- Pod security consistent across api/frontend/backup: `allowPrivilegeEscalation: false`,
  `drop: [ALL]`, `seccompProfile: RuntimeDefault`, `readOnlyRootFilesystem: true` where possible,
  `automountServiceAccountToken: false`. PostGIS init-chown is confined to a root initContainer with
  minimal capabilities; the main container runs as uid 999.
- Network: default-deny ingress + egress with precise allow rules (DNS, api↔postgis,
  frontend→api, ingress→api/frontend, backup→postgis, otel egress). PSA baseline enforced /
  restricted warned + audited on the namespace.
- API ingress enforces mTLS against `nars/nars-ca`; `/health` has a separate non-mTLS ingress
  restricted to cluster CIDRs.
- Backups: GPG AES256-encrypted, size sanity check, `concurrencyPolicy: Forbid`, 30-day retention.
- Dockerfiles pin base images by digest; apt/apk version pins documented with rationale in
  `.hadolint.yaml`; `nginx.nars-vite.conf` repeats the CSP in the static-asset block with a comment
  explaining `add_header` non-inheritance; OTel proxy uses a runtime-resolved variable.
- Linter configs (`.hadolint.yaml`, `.yamllint.yaml`) document every override with its rationale.
