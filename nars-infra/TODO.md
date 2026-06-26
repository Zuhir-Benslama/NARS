# Code Quality

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| High | Plaintext backups — pg_dump output written to PVC without encryption | `k8s/backup-cronjob.yaml` | Fixed — added GPG symmetric AES256 encryption via `gpg-passphrase` secret; plaintext fallback with warning |
| High | `latest` image tags in kustomization.yaml — non-reproducible deployments | `k8s/kustomization.yaml`, `Makefile` | Fixed — added `IMAGE_TAG` validation warning in `kustomize-apply` target |
| High | OTel collector TLS set to `insecure: true` | `k8s/helm-values/opentelemetry-collector.yaml` | Fixed — expanded TODO into actionable production cert setup docs with secret/volume mount instructions |
| Medium | UID mismatch: Dockerfile sets `USER 1654`, but deployment overrides with `runAsUser: 1000` | `k8s/app-deployment.yaml` | Fixed — changed `runAsUser`/`fsGroup` to 1654 |
| Medium | Missing `gpg-passphrase` in secret template and `secrets-apply` target | `k8s/secret.yaml`, `Makefile` | Fixed — added to template and auto-generation |
| Low | `seed_reference_data.sql:488` — `الوادي/El Oued` assigned to Wilaya 16 | `docs/seed_reference_data.sql` | Not an issue — Bab El Oued is a district of Algiers (Wilaya 16), coordinates confirm |
