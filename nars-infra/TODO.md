# nars-infra Code Quality

## ✅ Fixed
- `k8s/secret.yaml` — Placeholder values removed; file is now a pure reference
  template with no `stringData`. A `secrets-validate` pre-apply check rejects
  any `REPLACE_ME` values in kustomize output (see Makefile).
- `k8s/backup-cronjob.yaml` — `gpg-passphrase` is now required (was `optional: true`).
  Plaintext fallback path removed. `make secrets-apply` always generates a passphrase.
- `docker/Dockerfile.nars-postgis` — Already had HEALTHCHECK (`pg_isready`).
- `k8s/network-policy.yaml` — Already had `default-deny-egress` + egress allow rules.

## Notes (intentional design choices)
- `k8s/kustomization.yaml:48-53` — `latest` image tags for local dev.
  CI/CD is expected to override with pinned SHAs via `kustomize edit set image`.
  A warning is printed by `kustomize-apply` when `IMAGE_TAG=latest`.
- `k8s/kind-config.yaml:18,22` — `<YOUR_HOST_IP>` and `/absolute/path/to/...`
  placeholders. The Makefile generates the actual config dynamically.
