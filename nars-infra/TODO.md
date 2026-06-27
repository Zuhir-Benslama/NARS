# Code Quality — Issues

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Low | `AllowedHosts: "*"` in prod ConfigMap overrides `appsettings.json` fix — added detailed comment explaining K8s probe requirement | `k8s/configmap.yaml:15` | Fixed |
| Low | CSP header is a single 290+ char line — broken into multi-line for readability | `docker/nginx.nars-vite.conf:19` | Fixed |
| Low | GPG passphrase passed via `--passphrase` CLI arg — changed to `--passphrase-fd 0` with stdin pipe | `k8s/backup-cronjob.yaml:71` | Fixed |
