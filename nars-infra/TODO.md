# nars-infra — Code Quality TODO

## Infrastructure Code Quality

### Makefile
- [ ] **Makefile is well-structured**: Documented targets, idempotent operations, good patterns throughout.
- [ ] **Shell targets now functional**: `infra-lint-shell`, `infra-lint-docker`, `infra-lint-yaml` fixed and working.

### Docker
- Dockerfiles have `org.opencontainers.image.*` labels, SHELL directives, pinned versions.

### K8s
- Kustomize-based deployment, health checks, proper probes — no issues identified.

## Previous Issues (All Resolved)
All 19 previously identified infra issues have been closed.
