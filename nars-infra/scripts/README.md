# NARS Infra Scripts

Developer and deployment utilities. Not all of these are wired into the
Makefile targets — they are standalone tools invoked manually or by CI/docs
pipelines.

| File | Purpose | Invoked by |
|------|---------|-----------|
| `create_nars_db.sql` | Full database bootstrap run by the PostGIS image entrypoint: creates `nars_db`, PostGIS, EF migrations history, reference tables (wilayas/dairas/communes), users + RBAC, refresh tokens, feature registry, feature tables, error logs, and the AI draft features table. Uses psql meta-commands (`\gexec`, `\c`) so it must run via `psql`. | PostGIS Docker image init; `make cluster-up` (fresh clusters) |
| `postgis-migration-baseline.sql` | Idempotent seed of `__EFMigrationsHistory` so freshly-created databases are recognized as already migrated to the current schema, without running every historical EF migration. | PostGIS Docker image init |
| `create_national_admin.sh` | Bootstraps the first `national_admin` account directly in PostgreSQL (the only way — no API endpoint exists by design). Passes all values as psycopg2 parameters (no SQL injection). | Manual (run on the DB pod) |
| `kustomize-tag-rewrite.awk` | Rewrites image tags + `app.kubernetes.io/version` labels in `kubectl kustomize` output so `kustomize-apply` can pin per-run tags without editing `kustomization.yaml`. | `make/kustomize-apply` (see `make/deploy.mk`) |
| `render-mermaid-playwright.mjs` | Renders ` ```mermaid ` blocks from `docs/uml/*.md` to PNG via headless Firefox. Fails (non-zero) on missing input, render errors, or empty output so docs pipelines can't silently pass. | Manual / docs-workflow (not a Makefile target) |
| `png-to-pdf.py` | Converts all PNG files in a directory to a PDF (requires Pillow). | Manual (unsupported utility) |

## Database vs. migration SQL

The SQLFluff gate (`make infra-lint-sql`) covers `migrations/*.sql` and
`postgis-migration-baseline.sql` only. `create_nars_db.sql` is deliberately
excluded: sqlfluff is a SQL parser, and that file relies on psql
meta-commands (`\gexec`, `\c`) that a SQL parser cannot interpret.

The two files that define the AI draft features table
(`migrations/0001_create_ai_draft_features.sql` and section 10 of
`create_nars_db.sql`) must stay in sync by name — see the warning inside the
migration file.