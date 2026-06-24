# Code Quality Improvements — Complete

## Fixed

- **`scripts/png-to-pdf.py`** — Added argument validation, usage message (`-h`/`--help`), error handling for non-existent directories, type hints, and `if __name__ == "__main__"` guard.

- **`k8s/helm-values/opentelemetry-collector.yaml:56-58`** — Improved `insecure: true` TODO comment with specific TLS cert field names and mount guidance.

- **`k8s/kustomization.yaml:32-46`** — Expanded image tag comments with concrete `kustomize edit set image` examples for CI/CD SHA pinning.

## Verified

- yamllint: 0 warnings, 0 errors
