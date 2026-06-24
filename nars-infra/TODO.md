# Code Quality — All Issues Fixed

All 4 Makefile issues identified in the review have been resolved.

| Severity | Line | Fix |
|----------|------|-----|
| Medium | 409 | Pinned `ingress-nginx` URL from `main` → `controller-v1.12.0` tag for reproducible installs |
| Low | 780-781 | Replaced `cat file | kubectl apply -f -` with direct `kubectl apply -f file` |
| Low | 837-845 | Replaced `$(PWD)` with `$$(pwd)` for consistency with the rest of the Makefile |
| Low | 400-402 | Added timeout (60 iterations × 2s = 120s) to `until` loop in `cluster-wait` |
