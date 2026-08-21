# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: PostGIS backup/restore/admin + migrations.


.PHONY: db-get-pod
db-get-pod: ## Get the postgis pod name
	@$(POSTGIS_GET_POD_CMD) || echo ""

.PHONY: db-get-password
db-get-password: ## Get the postgis password from k8s secret (stderr only, non-CI safe)
	@pass=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null | base64 -d 2>/dev/null || true)
	@if [ -z "$$pass" ]; then
		echo "⚠ No postgis password found in secret 'nars-secrets'" >&2
	elif [ -t 2 ]; then
		echo "$$pass" >&2
	else
		echo "⚠ Refusing to print password to non-tty stderr (use 'make db-get-password' interactively)" >&2
		exit 1
	fi

.PHONY: db-backup
db-backup: ## Dump the PostGIS database to a local file
	@POD=$$($(POSTGIS_GET_POD_CMD) || true)
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@PASS=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null | base64 -d 2>/dev/null || true)
	@if [ -z "$$PASS" ]; then echo "✖ Could not read DB password — is nars-secrets deployed?"; exit 1; fi
	@echo "→ Backing up database '$(DB_NAME)' from pod $$POD..."
	@PREFIX=manual
	@$(_pg_dump_cmd)
	@echo "✓ Backup saved: $${FILE}.gz"
	@ls -lh "$${FILE}.gz"

.PHONY: db-restore
db-restore: ## Restore a backup. Usage: make db-restore FILE=backup/manual_nars_db_20250101_120000.sql.gz
	@POD=$$($(POSTGIS_GET_POD_CMD) || true)
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@if [ -z "$(FILE)" ]; then
		echo "✖ Usage: make db-restore FILE=backup/<manual|auto>_nars_db_<timestamp>.sql.gz";
		echo "";
		echo "Available backups:";
		ls -1 "$(BACKUP_DIR)"/*.sql.gz 2>/dev/null | sed 's/^/  /' || echo "  (none)";
		exit 1;
	fi
	@if [ ! -f "$(FILE)" ]; then echo "✖ File not found: $(FILE)"; exit 1; fi
	@if echo "$(FILE)" | grep -qE '[^a-zA-Z0-9._/-]|\.\.'; then
		echo "✖ FILE='$(FILE)' contains unexpected characters";
		exit 1;
	fi
	@PASS=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null | base64 -d 2>/dev/null || true)
	@if [ -z "$$PASS" ]; then echo "✖ Could not read DB password — is nars-secrets deployed?"; exit 1; fi
	@echo "→ Restoring '$(FILE)' into $(DB_NAME)..."
	@echo "  ⚠ This will OVERWRITE the current database."
	@if [ -t 0 ]; then
		read -p "  Continue? (yes/no): " confirm;
		if [ "$$confirm" != "yes" ]; then echo "  Cancelled."; exit 0; fi;
	else
		echo "  Non-interactive shell — refusing restore.";
		exit 1;
	fi
	@if echo "$(FILE)" | grep -q '\.gz$$'; then
		(echo "$$PASS"; gunzip -c "$(FILE)") | $(KUBECTL) exec -i "$$POD" -n "$(NAMESPACE)" -- \
			bash -c 'read -r _pw; PGPASSWORD="$$_pw" psql -U postgres -d "$(DB_NAME)"'
	else
		(echo "$$PASS"; cat "$(FILE)") | $(KUBECTL) exec -i "$$POD" -n "$(NAMESPACE)" -- \
			bash -c 'read -r _pw; PGPASSWORD="$$_pw" psql -U postgres -d "$(DB_NAME)"'
	fi
	@echo "✓ Restore complete"

.PHONY: db-shell
db-shell: ## Open an interactive psql shell inside the postgis pod
	@POD=$$($(POSTGIS_GET_POD_CMD) || true)
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found"; exit 1; fi
	@$(KUBECTL) exec -it "$$POD" -n "$(NAMESPACE)" -- psql -U postgres -d "$(DB_NAME)"

.PHONY: db-admin
db-admin: .env prerequisites ## Create national admin with one-time generated credentials
	@export NON_INTERACTIVE=1
	@export ADMIN_NAME="National Admin"
	@export ADMIN_EMAIL="admin@nars.dz"
	@export ADMIN_PHONE="+213000000000"
	@echo ""
	@echo "→ Generating one-time national admin credentials..."
	@command -v openssl >/dev/null 2>&1 || { echo "✖ openssl is not installed"; exit 1; }
	@ADMIN_USERNAME="admin_$$(openssl rand -hex 4)"
	@ADMIN_PASSWORD="$$(openssl rand -base64 12)"
	@export ADMIN_USERNAME ADMIN_PASSWORD
	@echo "  Username: $${ADMIN_USERNAME}"
	# SECURITY: do not echo the password here. create_national_admin.sh prints
	# it to stderr only, so piping this target's stdout (e.g. `| tee x.log`)
	# never captures the credential — same convention as db-get-password.
	@echo ""
	@bash nars-infra/scripts/create_national_admin.sh
	@echo ""
	@echo "→ Done. Save the credentials above — they will not be shown again."


.PHONY: postgis-password-sync
postgis-password-sync: ## Align postgres user password with POSTGRES_PASSWORD (for persisted volumes)
# Password piped via stdin to avoid exposure in kubectl's remote command arguments.
# Uses $$POSTGRES_PASSWORD (shell env var) instead of $(POSTGRES_PASSWORD) (Make expansion)
# to avoid breakage if the value contains shell metacharacters like single quotes.
	@echo "→ Syncing postgres password..."
	@printf '%s\n' "$$POSTGRES_PASSWORD" | \
		$(KUBECTL) exec -i -n "$(NAMESPACE)" deployment/postgis -- \
		bash -c 'read -r _pgpw; printf "ALTER USER postgres WITH PASSWORD '\''%s'\'';\n" "$$_pgpw" | psql -U postgres -d postgres -v ON_ERROR_STOP=1' >/dev/null
	@echo "✓ Postgres password synced"

.PHONY: postgis-migration-baseline
postgis-migration-baseline: ## Backfill EF migration history for pre-existing schemas
	@echo "→ Ensuring EF migration history baseline..."
	@$(KUBECTL) exec -i -n "$(NAMESPACE)" deployment/postgis -- \
		psql -U postgres -d "$(DB_NAME)" -v ON_ERROR_STOP=1 >/dev/null \
		< "nars-infra/scripts/postgis-migration-baseline.sql"
	@echo "✓ EF migration history baseline ensured"

.PHONY: db-migrate-nars
db-migrate-nars: ## Apply NARS SQL migrations (nars-infra/migrations/*.sql) to the deployed DB (idempotent)
	@echo "→ Applying NARS SQL migrations from $(MIGRATIONS_DIR)..."
	@count=0; \
	for f in "$(MIGRATIONS_DIR)"/*.sql; do \
		[ -f "$$f" ] || continue; \
		echo "→ Applying $$(basename "$$f")..."; \
		cat "$$f" | $(KUBECTL) exec -i -n "$(NAMESPACE)" deployment/postgis -- \
			psql -U postgres -d "$(DB_NAME)" -v ON_ERROR_STOP=1 >/dev/null || exit 1; \
		count=$$((count + 1)); \
	done; \
	if [ "$$count" -eq 0 ]; then \
		echo "✖ No migration files found in $(MIGRATIONS_DIR) — nothing was applied"; \
		exit 1; \
	fi
	@echo "✓ NARS SQL migrations applied ($$count file(s))"
