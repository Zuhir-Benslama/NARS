# AI Agent Guidelines — NARS Project

## CRITICAL: Data Safety Rules

These rules exist because data has been destroyed multiple times. Violating them causes real harm.

### Rule 1: NEVER Delete or Overwrite Data Without Explicit User Approval

- Never run `DROP DATABASE`, `DROP TABLE`, `TRUNCATE`, `DELETE FROM` without user confirmation
- Never run `pg_resetwal`, `pg_resetxlog`, or any WAL recovery tool without explicit written approval
- Never run destructive git commands (`git push --force`, `git reset --hard`, `git clean -fd`) without confirmation
- Never delete files that contain user data, configurations, or database volumes

### Rule 2: Never Assume a Backup Exists

- Before any destructive operation, check for backups: `ls backup/`
- If no backup exists, tell the user and help create one before proceeding
- Never say "it's fine, we have a backup" unless you have verified the backup exists and is recent

### Rule 3: Database Operations Require Extreme Caution

- `pg_resetwal -f` DESTROYS uncommitted transaction data. It is a last resort. Ask first.
- `DROP DATABASE ... WITH (FORCE)` kills connections and destroys data. Ask first.
- Always prefer `pg_dump` before any recovery operation
- When in doubt, stop and ask the user

### Rule 4: Kubernetes Storage is Persistent

- PersistentVolumes in Kind survive `kind delete cluster` — they live on the host filesystem
- Deleting PVs or PVCs destroys the data inside them
- Never run `kubectl delete pvc` or `rm -rf data/` without confirmation
- Before reinitializing a database pod, always check if data already exists in the PV

### Rule 5: Check Existing State Before Acting

- Before creating users/tables/data, check if they already exist
- Before applying migrations, check if they've already been applied
- Before running seed scripts, check if the data is already present
- Use `kubectl exec ... -- psql` or API calls to inspect current state

### Rule 6: When Things Break, Diagnose First

- Never jump to "nuke and recreate" as the first solution
- Read error logs: `kubectl logs`, `journalctl`, application logs
- Understand WHY something is broken before attempting a fix
- Ask the user for context — they may know something you don't

## Operational Rules

### Make Targets

- `make cluster-up` is the primary entry point — it runs full initialization
- `make smoke-test` verifies the cluster is working
- `make db-backup` and `make db-restore` handle database backups
- Check `Makefile` before assuming a target exists

### Code Quality

- Run `dotnet build --no-restore` to check for compilation errors
- Run `dotnet test` to verify tests pass
- Run `make lint` for cross-project linting
- All warnings are treated as errors in the API project (`TreatWarningsAsErrors=true`)

### Environment

- Rootless Docker on Fedora — container uid 0 ≠ host root
- Kind cluster name: `nars-cluster`
- Namespace: `nars`
- Database: PostGIS in `nars_db`

## Remember

Data loss is permanent. Code bugs can be fixed. Broken tests can be repaired.
A destroyed database with no backup is gone forever. Always err on the side of caution.
