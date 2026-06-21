# nars-infra — Code Quality Issues

## Fixed

- [x] **Medium: Seed data — Daira 452 mis-assigned to wilaya 39** — Changed `wilaya_id` from 39 to 16 (Algiers). Name/coordinates now match the correct wilaya.
- [x] **Low: Backup cronjob — DB connection params hardcoded** — Moved `PGHOST`, `PGPORT`, `PGUSER`, `PGDATABASE` to `configmap.yaml`; referenced via `configMapKeyRef` in backup cronjob.
- [x] **Low: No `.dockerignore`** — Already existed at repo root with comprehensive ignores. Verified — no change needed.
- [x] **Low: `render-mermaid.mjs` pollutes globals** — Added deprecation notice pointing to `render-mermaid-playwright.mjs` as the preferred alternative.
- [x] **Info: Curl version pin in Dockerfile.nars-api** — Changed from exact patch `curl=8.5.0-2ubuntu10.9` to series wildcard `curl=8.5.0-2ubuntu10*`. Satisfies hadolint DL3008 while tolerating security patch bumps.

## Remaining

- (none)
