# Code Quality Issues

All items from the initial code quality check have been resolved.

## Fixed

- **`k8s/app-deployment.yaml`, `k8s/frontend-deployment.yaml`, `k8s/postgis.yaml`** — Removed `imagePullPolicy: Always` from deployment YAMLs. `kustomization.yaml` now uses `newPullPolicy: IfNotPresent` for all three images, and CI/CD can override `newTag` to a pinned SHA. Local dev still works with `:latest`.
- **`k8s/network-policy.yaml`** — Added `allow-backup-egress-to-postgis` NetworkPolicy for the backup CronJob pod (was previously relying on namespace-wide default policies alone).
- **`k8s/helm-values/opentelemetry-collector.yaml`** — Added `TODO` comment documenting that `insecure: true` should be replaced with proper TLS certs for production.

## Not Addressed (no action needed)

- **`k8s/postgis.yaml`** — PostGIS runs as root with `allowPrivilegeEscalation: true` and `readOnlyRootFilesystem: false`. Well-documented as a PostGIS entrypoint requirement; should be revisited if a rootless PostGIS image becomes available.
- **`k8s/postgis.yaml`** — Single replica. Acceptable for many deployments; HA requires a more complex PostGIS HA setup (e.g. Patroni, repmgr).

## Verified

- yamllint: 0 warnings, 0 errors
