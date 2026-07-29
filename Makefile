SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.ONESHELL:
.DEFAULT_GOAL := help

CLUSTER_NAME       ?= nars
NAMESPACE          ?= nars
DOMAIN             ?= nars.dz
APP_PORT           ?= 8080
APP_TLS_PORT       ?= 8443
K8S_DIR            ?= nars-infra/k8s
DOCKER_DIR         ?= nars-infra/docker
DOCKER_ORG         ?= zuhirbenslama
DOCKER_USERNAME    ?= zuhirbenslama
KUBECTL            ?= kubectl
DOCKER_TOKEN       ?=
BACKUP_DIR         ?= backup
DB_NAME            ?= nars_db
POSTGRES_DATA_DIR  ?= data/nars/postgis
REGISTRY_IMAGES    := nars-api nars-postgis nars-vite nars-backup
SCALABLE_DEPLOYS   := postgis nars-api nars-frontend
INGRESS_NGINX_VERSION ?= v1.12.0
METRICS_SERVER_VERSION ?= v0.7.2
LOCAL_PATH_PROVISIONER_VERSION ?= v0.0.30
YAMLLINT_IMAGE     ?= cytopia/yamllint:1.36.0
OBSERVABILITY_NAMESPACE ?= observability
LOG_DIR                ?= /tmp/nars
POSTGIS_GET_POD_CMD = $(KUBECTL) get pod -n "$(NAMESPACE)" -l app.kubernetes.io/name=postgis -o jsonpath='{.items[0].metadata.name}' 2>/dev/null

# ─── Secrets ──────────────────────────────────────────────────
# Auto-generate .env with stable secrets if missing.
# Make auto-remakes missing included files and re-execs, so
# all targets see consistent POSTGRES_PASSWORD / JWT_SECRET.
# Generate a random base64 string using openssl (preferred) or python3.
# Used in recipe contexts (where $$1 is the byte count).
_rnd_cmd = if command -v openssl >/dev/null 2>&1; then \
	openssl rand -base64 "$$1" | tr -d '\n'; \
else \
	python3 -c "import base64,os; print(base64.b64encode(os.urandom(int(\"$$1\"))).decode())"; \
fi

# _RND shell function + $$(_RND N) expansion rely on .ONESHELL.
.env:
	@echo "# Auto-generated — DO NOT COMMIT" > $@; \
	_RND() { $(_rnd_cmd); }; \
	echo "POSTGRES_PASSWORD=$$(_RND 32)" >> $@; \
	echo "JWT_SECRET=$$(_RND 32)" >> $@; \
	echo "GPG_PASSPHRASE=$$(_RND 32)" >> $@; \
	echo "GRAFANA_PASSWORD=$$(_RND 12)" >> $@; \
	chmod 600 $@; \
	echo "→ Created $@ with fresh secrets (permissions: 600)"

-include .env

# Fallback values — only used if .env is missing and system has neither
# openssl nor python3 (unlikely on any modern OS).
# Empty defaults ensure secrets-protect targets fail fast rather than
# proceeding with guessable credentials.
POSTGRES_PASSWORD  ?=
JWT_SECRET         ?=
GPG_PASSPHRASE     ?=
GRAFANA_PASSWORD   ?=
export POSTGRES_PASSWORD JWT_SECRET GPG_PASSPHRASE GRAFANA_PASSWORD

.PHONY: help
help: ## Show available targets
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-28s\033[0m %s\n", $$1, $$2}'

# ─── Prerequisites ──────────────────────────────────────────

.PHONY: prerequisites
prerequisites: ## Check that all required tools are installed
	@echo "→ Checking prerequisites..."
	@command -v kind >/dev/null 2>&1 || { echo "✖ kind is not installed → https://kind.sigs.k8s.io/docs/user/quick-start/"; exit 1; }
	@command -v kubectl >/dev/null 2>&1 || { echo "✖ kubectl is not installed"; exit 1; }
	@command -v mkcert >/dev/null 2>&1 || { echo "✖ mkcert is not installed → https://github.com/FiloSottile/mkcert"; exit 1; }
	@command -v docker >/dev/null 2>&1 || { echo "✖ docker is not installed"; exit 1; }
	@echo "✓ All prerequisites met"

.PHONY: _check-secrets
_check-secrets: ## Fail fast if critical secrets are empty (prevents deploying with insecure defaults)
	@if [ -z "$(POSTGRES_PASSWORD)" ] || [ -z "$(JWT_SECRET)" ]; then \
		echo "✖ Secrets not configured — run 'make .env' to generate them"; \
		exit 1; \
	fi
	@if [ -z "$(GPG_PASSPHRASE)" ] || [ -z "$(GRAFANA_PASSWORD)" ]; then \
		echo "✖ GPG_PASSPHRASE or GRAFANA_PASSWORD not set — run 'make .env' to generate them"; \
		exit 1; \
	fi

# ─── Cluster Lifecycle ──────────────────────────────────────

.PHONY: cluster-up
cluster-up: prerequisites _check-secrets ## Full bootstrap: create cluster, build images, deploy everything
	$(MAKE) cluster-create
	$(MAKE) kubeconfig-fix
	$(MAKE) ingress-install
	$(MAKE) ingress-wait
	$(MAKE) metrics-install
	$(MAKE) metrics-wait
	$(MAKE) storage-provisioner-install
	$(MAKE) storage-provisioner-wait
	$(MAKE) images-build
	$(MAKE) tls-generate
	$(MAKE) ca-secret
	$(MAKE) secrets-apply
	$(MAKE) images-load
	$(MAKE) kustomize-apply
	@echo ""
	@echo "✓ Cluster '$(CLUSTER_NAME)' is ready!"
	@echo ""
	@echo "  Proxy:         make proxy-up"
	@echo "  Mobile app:    make adb-reverse"
	@echo "  Smoke test:    make smoke-test"
	@echo "  Visit:         http://localhost:$(APP_PORT)/"
	@echo "  Stop proxy:    make proxy-down"
	@echo "  Stop pods:     make cluster-stop"
	@echo "  Tear down:     make cluster-down (data preserved)"
	@echo "  Destroy data:  make cluster-clean"

.PHONY: cluster-up-full
cluster-up-full: ## Full bootstrap including observability stack
	$(MAKE) cluster-up
	$(MAKE) observability-install
	@echo ""
	@echo "✓ Cluster '$(CLUSTER_NAME)' with observability is ready!"
	@echo "  Port-forward:  make observability-port-forward"
	@echo "  Visit:         http://localhost:$(APP_PORT)/"

.PHONY: namespace-ensure
namespace-ensure: ## Ensure $(NAMESPACE) namespace exists (idempotent)
	@$(KUBECTL) create namespace "$(NAMESPACE)" --dry-run=client -o yaml | $(KUBECTL) apply -f -

.PHONY: cluster-down
cluster-down: proxy-down _pre-cluster-down-backup ## Delete the kind cluster (auto-backs up data first)
	@echo "→ Deleting cluster '$(CLUSTER_NAME)'..."
	@kind delete cluster --name "$(CLUSTER_NAME)" 2>/dev/null || true
	@docker rm -f kube-proxy 2>/dev/null || true
	@echo "✓ Cluster deleted (postgis data preserved at $(POSTGRES_DATA_DIR))"

# Shared pg_dump + gzip logic. Expects $$POD and $$PASS to be set in shell scope.
# SECURITY: Password piped via stdin to avoid exposure in container's process table.
_pg_dump_cmd = \
	mkdir -p "$(BACKUP_DIR)"; \
	TIMESTAMP=$$(date +"%Y%m%d_%H%M%S"); \
	FILE="$(BACKUP_DIR)/nars_db_$${TIMESTAMP}.sql"; \
	printf '%s\n' "$$PASS" | \
		$(KUBECTL) exec -i "$$POD" -n "$(NAMESPACE)" -- \
		bash -c 'read -r _pw; PGPASSWORD="$$_pw" pg_dump -U postgres -d "$(DB_NAME)" --no-owner' > "$$FILE"; \
	gzip -f "$$FILE"

# Internal: auto-backup before cluster teardown. Blocks if backup fails.
.PHONY: _pre-cluster-down-backup
_pre-cluster-down-backup:
	@POD=$$($(POSTGIS_GET_POD_CMD) 2>/dev/null); \
	if [ -z "$$POD" ]; then \
		echo "→ No postgis pod running — skipping auto-backup"; \
		exit 0; \
	fi; \
	echo "→ Auto-backing up database before cluster teardown..."; \
	PASS=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null | base64 -d 2>/dev/null); \
	if [ -z "$$PASS" ]; then \
		echo "  ⚠ Could not read DB password — skipping backup"; \
		exit 0; \
	fi; \
	$(_pg_dump_cmd); \
	if [ ! -s "$${FILE}.gz" ]; then \
		echo "✖ Auto-backup FAILED — refusing to tear down cluster"; \
		rm -f "$${FILE}.gz"; \
		exit 1; \
	fi; \
	echo "✓ Auto-backup saved: $${FILE}.gz"

.PHONY: cluster-rebuild
cluster-rebuild: cluster-down cluster-up ## Delete and recreate the cluster (preserves data)

.PHONY: cluster-rebuild-full
cluster-rebuild-full: cluster-down cluster-up-full ## Delete and recreate the cluster with observability (preserves data)

.PHONY: cluster-reset
cluster-reset: cluster-clean cluster-up-full ## Wipe data, recreate cluster with observability

# cluster-down auto-backs up pg_dump before teardown — order is intentional
.PHONY: cluster-clean
cluster-clean: ## Delete cluster AND wipe postgis data (irreversible!)
	@echo "⚠  WARNING: This will DESTROY all postgis data at $(POSTGRES_DATA_DIR)"
	@if [ -t 0 ]; then \
		read -p "  Type the cluster name '$(CLUSTER_NAME)' to confirm: " confirm; \
		if [ "$$confirm" != "$(CLUSTER_NAME)" ]; then echo "  Cancelled."; exit 1; fi; \
	else \
		echo "  Non-interactive shell — refusing destructive operation."; \
		exit 1; \
	fi
	$(MAKE) cluster-down
	@echo "→ Wiping postgis data..."
	@if rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null; then \
		echo "✓ Data wiped"; \
	elif sudo -n rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null; then \
		echo "✓ Data wiped (via sudo)"; \
	else \
		echo "✖ Failed to remove $(POSTGRES_DATA_DIR) — check permissions"; \
		exit 1; \
	fi

.PHONY: cluster-status
cluster-status: ## Show cluster resources
	@echo "=== Nodes ==="
	@$(KUBECTL) get nodes 2>/dev/null || echo "(cluster not running)"
	@echo ""
	@echo "=== Namespace: $(NAMESPACE) ==="
	@$(KUBECTL) get all,ingress,pvc -n "$(NAMESPACE)" 2>/dev/null || echo "(not deployed)"
	@echo ""
	@echo "=== Namespace: $(OBSERVABILITY_NAMESPACE) ==="
	@$(KUBECTL) get all,pods -n "$(OBSERVABILITY_NAMESPACE)" 2>/dev/null || echo "(not deployed)"
	@echo ""
	@echo "=== Endpoints ==="
	@$(KUBECTL) get endpoints -n "$(NAMESPACE)" 2>/dev/null || true

.PHONY: cluster-logs
cluster-logs: ## Tail logs from all pods in the namespace
	@$(KUBECTL) logs -n "$(NAMESPACE)" --all-containers --tail=50 --follow 2>/dev/null \
		|| echo "! Failed to tail logs — no running pods in '$(NAMESPACE)' or kubectl error"

.PHONY: cluster-port-forward
cluster-port-forward: proxy-up ## Deprecated — use 'proxy-up' directly.
	@echo "⚠ DEPRECATED: 'cluster-port-forward' is deprecated — use 'proxy-up' instead." >&2

PROXY_CONTAINER ?= kind-proxy

.PHONY: port-forward-start
port-forward-start: ## Start kubectl port-forward inside the kind container (background)
	@echo "→ Starting port-forward inside kind container..."
	@docker exec $(CLUSTER_NAME)-control-plane sh -c 'pkill -f "port-forward.*ingress-nginx" 2>/dev/null; true' 2>/dev/null || true
	@sleep 0.5
	@docker exec -d $(CLUSTER_NAME)-control-plane kubectl port-forward --address 0.0.0.0 \
		-n ingress-nginx service/ingress-nginx-controller $(APP_PORT):80 > /dev/null 2>&1
	@docker exec -d $(CLUSTER_NAME)-control-plane kubectl port-forward --address 0.0.0.0 \
		-n ingress-nginx service/ingress-nginx-controller $(APP_TLS_PORT):443 > /dev/null 2>&1
	@sleep 2
	@echo "✓ Port-forward started inside kind container"

.PHONY: port-forward-stop
port-forward-stop: ## Stop port-forward inside the kind container
	@docker exec $(CLUSTER_NAME)-control-plane sh -c 'pkill -f "port-forward.*ingress-nginx" 2>/dev/null; true' 2>/dev/null || true
	@echo "✓ Port-forward stopped"

.PHONY: proxy-up
proxy-up: port-forward-start ## Start Docker socat bridge: host:$(APP_PORT) → kind:$(APP_PORT)
	@echo "→ Setting up socat bridge container..."
	@docker rm -f "$(PROXY_CONTAINER)" 2>/dev/null || true
	@sleep 0.5
	@docker run -d --name "$(PROXY_CONTAINER)" --rm \
		-p 0.0.0.0:$(APP_PORT):$(APP_PORT) \
		-p 0.0.0.0:$(APP_TLS_PORT):$(APP_TLS_PORT) \
		--network kind \
		--entrypoint sh \
		alpine/socat \
		-c "socat tcp-l:$(APP_PORT),fork,reuseaddr tcp:$(CLUSTER_NAME)-control-plane:$(APP_PORT) & socat tcp-l:$(APP_TLS_PORT),fork,reuseaddr tcp:$(CLUSTER_NAME)-control-plane:$(APP_TLS_PORT) & wait -n" > /dev/null
	@echo "→ Waiting for proxy to be ready..."
	@for i in $$(seq 1 15); do \
		running=$$(docker inspect -f '{{.State.Running}}' "$(PROXY_CONTAINER)" 2>/dev/null || echo "false"); \
		if [ "$$running" != "true" ]; then \
			echo "✖ socat container '$(PROXY_CONTAINER)' exited unexpectedly"; \
			docker logs "$(PROXY_CONTAINER)" 2>&1 | tail -5; \
			exit 1; \
		fi; \
		status=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 2 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "000"); \
		if [ "$$status" != "000" ]; then break; fi; \
		sleep 1; \
	done; \
	status=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 2 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "000"); \
	if [ "$$status" = "200" ] || [ "$$status" = "302" ]; then \
		echo "✓ Proxy ready ($$status)"; \
	else \
		echo "⚠ Proxy may not be reachable (status: $$status — check port-forward or rootless Docker networking)"; \
	fi
	@echo ""
	@echo "✓ App accessible at http://localhost:$(APP_PORT)/"
	@echo "  Health:      http://localhost:$(APP_PORT)/api/health"
	@echo "  Mobile app:  make adb-reverse    (if connected via USB)"
	@echo "  Smoke test:  make smoke-test"
	@echo "  Stop proxy:  make proxy-down"

.PHONY: proxy-down
proxy-down: port-forward-stop ## Stop the socat bridge and port-forward
	@echo "→ Stopping socat bridge..."
	@docker rm -f "$(PROXY_CONTAINER)" 2>/dev/null || true
	@echo "✓ Proxy stopped"

.PHONY: adb-reverse
adb-reverse: ## Forward phone:$(APP_PORT) → host:$(APP_PORT) via USB (for mobile dev)
	@echo "→ Setting up adb reverse proxy..."
	@adb reverse tcp:$(APP_PORT) tcp:$(APP_PORT) 2>&1
	@echo "✓ Phone can now reach the API at http://localhost:$(APP_PORT)/"
	@echo "  (Lasts while USB is connected; re-run after USB disconnect/reconnect)"

.PHONY: proxy-status
proxy-status: ## Show proxy status
	@echo "=== Port-forward (kind container) ==="
	@docker exec $(CLUSTER_NAME)-control-plane ss -tlnp 2>/dev/null | grep -E '$(APP_PORT)|$(APP_TLS_PORT)' || echo "  NOT RUNNING"
	@echo ""
	@echo "=== socat bridge container ==="
	@docker ps --filter name=$(PROXY_CONTAINER) --format '  {{.ID}} {{.Status}} {{.Image}}' 2>/dev/null || echo "  NOT RUNNING"
	@echo ""
	@echo "=== App health ==="
	@curl -s -o /dev/null -w "  HTTP %{http_code}\n" --connect-timeout 3 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "  UNREACHABLE"

# ─── Smoke Test ────────────────────────────────────────────

SMOKE_BASE_URL ?= http://localhost:$(APP_PORT)

.PHONY: smoke-test
smoke-test: ## Post-deploy smoke test: verify /health, frontend, and API auth
	@echo "→ Running smoke tests against $(SMOKE_BASE_URL)..."
	@echo ""
	@failed=0; \
	pass() { echo "  ✓ $$1"; }; \
	fail() { echo "  ✖ $$1"; failed=$$((failed + 1)); }; \
	\
	echo "  1. Health endpoint..."; \
	health=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/health" 2>/dev/null || echo "000"); \
	if [ "$$health" = "200" ]; then \
		body=$$(curl -s --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/health" 2>/dev/null); \
		if echo "$$body" | grep -q "^Healthy$$"; then \
			pass "/health → 200 Healthy"; \
		else \
			fail "/health → 200 but body unexpected: $$body"; \
		fi; \
	else \
		fail "/health → $$health (expected 200)"; \
	fi; \
	\
	echo "  2. Frontend reachability..."; \
	frontend=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/" 2>/dev/null || echo "000"); \
	if [ "$$frontend" = "200" ]; then \
		pass "/ → 200"; \
	elif [ "$$frontend" = "302" ] || [ "$$frontend" = "301" ]; then \
		pass "/ → $$frontend (redirect — SPA serving correctly)"; \
	else \
		fail "/ → $$frontend (expected 200 or redirect)"; \
	fi; \
	\
	echo "  3. API auth endpoint..."; \
	auth=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 \
		-X POST "$(SMOKE_BASE_URL)/api/signin" \
		-H "Content-Type: application/json" \
		-d '{"username":"nonexistent","password":"bad"}' 2>/dev/null || echo "000"); \
	if [ "$$auth" = "401" ]; then \
		pass "POST /api/signin → 401 (auth endpoint alive, bad creds rejected)"; \
	else \
		fail "POST /api/signin → $$auth (expected 401)"; \
	fi; \
	\
	echo ""; \
	if [ "$$failed" -eq 0 ]; then \
		echo "✓ All smoke tests passed!"; \
	else \
		echo "✖ $$failed smoke test(s) failed"; \
		exit 1; \
	fi

.PHONY: cluster-stop
cluster-stop: ## Scale all deployments to 0 (stop pods, keep cluster)
	@echo "→ Stopping all pods..."
	@for deploy in $(SCALABLE_DEPLOYS); do
		exists=$$($(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" 2>/dev/null && echo "true" || echo "false")
		if [ "$$exists" = "false" ]; then
			echo "  ⚠ $$deploy not found, skipping"
			continue
		fi
		saved="$(BACKUP_DIR)/replicas/$$deploy.txt"
		replicas=$$($(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" \
			-o jsonpath='{.spec.replicas}' 2>/dev/null || echo "1")
		mkdir -p "$(BACKUP_DIR)/replicas"
		echo "$$replicas" > "$$saved"
		$(KUBECTL) scale deployment "$$deploy" -n "$(NAMESPACE)" --replicas=0
		echo "  ✓ $$deploy → 0 (was $$replicas)"
	done
	@echo "✓ All pods stopped. Run 'make cluster-start' to resume."

.PHONY: cluster-start
cluster-start: ## Scale all deployments back to their original replica count
	@echo "→ Starting pods..."
	@for deploy in $(SCALABLE_DEPLOYS); do
		saved="$(BACKUP_DIR)/replicas/$$deploy.txt"
		if [ -f "$$saved" ]; then
			replicas=$$(cat "$$saved")
		else
			replicas=1
		fi
		$(KUBECTL) scale deployment "$$deploy" -n "$(NAMESPACE)" --replicas=$$replicas
		echo "  ✓ $$deploy → $$replicas"
	done
	@echo "→ Waiting for deployments..."
	@for deploy in $(SCALABLE_DEPLOYS); do
		$(KUBECTL) wait --namespace "$(NAMESPACE)" \
			--for=condition=Available "deployment/$$deploy" --timeout=180s 2>/dev/null || true
	done
	@echo "✓ All pods running"

.PHONY: cluster-restart
cluster-restart: cluster-stop cluster-start ## Stop all pods, then start them again

.PHONY: db-get-pod
db-get-pod: ## Get the postgis pod name
	@$(POSTGIS_GET_POD_CMD) || echo ""

.PHONY: db-get-password
db-get-password: ## Get the postgis password from k8s secret
	@$(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null \
		| base64 -d 2>/dev/null || echo ""

.PHONY: db-backup
db-backup: ## Dump the PostGIS database to a local file
	@POD=$$($(POSTGIS_GET_POD_CMD))
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@PASS=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' | base64 -d)
	@if [ -z "$$PASS" ]; then echo "✖ Could not read DB password — is nars-secrets deployed?"; exit 1; fi
	@echo "→ Backing up database '$(DB_NAME)' from pod $$POD..."
	@$(_pg_dump_cmd)
	@echo "✓ Backup saved: $${FILE}.gz"
	@ls -lh "$${FILE}.gz"

.PHONY: db-restore
db-restore: ## Restore a backup. Usage: make db-restore FILE=backup/nars_db_20250101_120000.sql.gz
	@POD=$$($(POSTGIS_GET_POD_CMD))
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@if [ -z "$(FILE)" ]; then \
		echo "✖ Usage: make db-restore FILE=backup/nars_db_<timestamp>.sql.gz"; \
		echo ""; \
		echo "Available backups:"; \
		ls -1 "$(BACKUP_DIR)"/*.sql.gz 2>/dev/null | sed 's/^/  /' || echo "  (none)"; \
		exit 1; \
	fi
	@if [ ! -f "$(FILE)" ]; then echo "✖ File not found: $(FILE)"; exit 1; fi
	@if echo "$(FILE)" | grep -qE '[^a-zA-Z0-9._/-]'; then \
		echo "✖ FILE='$(FILE)' contains unexpected characters"; \
		exit 1; \
	fi
	@PASS=$$($(KUBECTL) get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' | base64 -d)
	@if [ -z "$$PASS" ]; then echo "✖ Could not read DB password — is nars-secrets deployed?"; exit 1; fi
	@echo "→ Restoring '$(FILE)' into $(DB_NAME)..."
	@echo "  ⚠ This will OVERWRITE the current database."
	@if [ -t 0 ]; then \
		read -p "  Continue? (yes/no): " confirm; \
		if [ "$$confirm" != "yes" ]; then echo "  Cancelled."; exit 0; fi; \
	else \
		echo "  Non-interactive shell — refusing restore."; \
		exit 1; \
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
	@POD=$$($(POSTGIS_GET_POD_CMD))
	@if [ -z "$$POD" ]; then echo "✖ No postgis pod found"; exit 1; fi
	@$(KUBECTL) exec -it "$$POD" -n "$(NAMESPACE)" -- psql -U postgres -d "$(DB_NAME)"

.PHONY: db-admin
db-admin: export NON_INTERACTIVE := 1
db-admin: export ADMIN_NAME := National Admin
db-admin: export ADMIN_EMAIL := admin@nars.dz
db-admin: export ADMIN_PHONE := +213000000000
db-admin: .env prerequisites ## Create national admin with one-time generated credentials
	@echo ""
	@echo "→ Generating one-time national admin credentials..."
	@command -v openssl >/dev/null 2>&1 || { echo "✖ openssl is not installed"; exit 1; }
	@ADMIN_USERNAME="admin_$$(openssl rand -hex 4)"
	@ADMIN_PASSWORD="$$(openssl rand -base64 12)"
	@export ADMIN_USERNAME ADMIN_PASSWORD
	@echo "  Username: $${ADMIN_USERNAME}"
	@echo "  Password: $${ADMIN_PASSWORD}"
	@echo ""
	@bash nars-infra/scripts/create_national_admin.sh
	@echo ""
	@echo "→ Done. Save the credentials above — they will not be shown again."

# ─── Individual Steps ───────────────────────────────────────

.PHONY: cluster-create
cluster-create: ## Create the kind cluster with host-mounted postgis data (idempotent)
	@if kind get clusters 2>/dev/null | grep -q "^$(CLUSTER_NAME)$$"; then
		echo "→ Cluster '$(CLUSTER_NAME)' already exists"
	else
		echo "→ Creating postgis data directory at $(POSTGRES_DATA_DIR)..."
		mkdir -p "$(POSTGRES_DATA_DIR)"
		chmod 750 "$(POSTGRES_DATA_DIR)" 2>/dev/null || true
		echo "→ Generating kind config..."
		DATA_DIR="$(POSTGRES_DATA_DIR)"
		if echo "$$DATA_DIR" | grep -qv '^/'; then
			DATA_DIR="$$(cd "$$DATA_DIR" && pwd)"
		fi
		KIND_CFG=$$(mktemp /tmp/kind-$(CLUSTER_NAME)-XXXXXX.yaml); \
		trap 'rm -f "$$KIND_CFG"' EXIT; \
		{ \
			echo 'kind: Cluster'; \
			echo 'apiVersion: kind.x-k8s.io/v1alpha4'; \
			echo "name: $(CLUSTER_NAME)"; \
			echo 'kubeadmConfigPatches:'; \
			echo '  - |'; \
			echo '    kind: ClusterConfiguration'; \
			echo '    apiServer:'; \
			echo '      certSANs:'; \
			echo '        - localhost'; \
			echo '        - 127.0.0.1'; \
			echo '        - 0.0.0.0'; \
			echo 'nodes:'; \
			echo '  - role: control-plane'; \
			echo '    extraMounts:'; \
			echo "      - hostPath: $$DATA_DIR"; \
			echo '        containerPath: /mnt/nars/postgis'; \
		} > "$$KIND_CFG"
		echo "→ Creating kind cluster '$(CLUSTER_NAME)'..."
		kind create cluster --name "$(CLUSTER_NAME)" --config "$$KIND_CFG"
		echo "✓ Cluster created"
	fi
	$(MAKE) cluster-wait

.PHONY: cluster-wait
cluster-wait: ## Wait for API server and nodes to be ready
	@echo "→ Waiting for API server and nodes..."
	@if ! $(KUBECTL) get nodes >/dev/null 2>&1; then \
		if docker info 2>/dev/null | grep -q "rootless"; then \
			echo "→ Rootless Docker detected — fixing kubeconfig first"; \
			$(MAKE) kubeconfig-fix; \
		fi; \
		i=0; until $(KUBECTL) get nodes 2>/dev/null; do \
			sleep 2; i=$$((i + 1)); \
			[ "$$i" -ge 60 ] && { echo "Timed out waiting for nodes"; exit 1; }; \
		done; \
	fi; \
	$(KUBECTL) wait --for=condition=Ready node --all --timeout=120s
	@echo "✓ Cluster ready"

.PHONY: kubeconfig-fix
kubeconfig-fix: ## Patch kubeconfig for rootless Docker (port 16443 via kube-proxy socat)
	@if docker info 2>/dev/null | grep -q "rootless"; then \
		echo "→ Rootless Docker detected — fixing kubeconfig port"; \
		KUBE_PROXY="kube-proxy"; \
		if ! docker ps --format "{{.Names}}" 2>/dev/null | grep -q "^$$KUBE_PROXY$$"; then \
			echo "→ Creating socat bridge (16443 -> $(CLUSTER_NAME)-control-plane:6443)..."; \
			docker run -d --name "$$KUBE_PROXY" --rm \
				-p 127.0.0.1:16443:16443 \
				--network kind \
				alpine/socat \
				tcp-listen:16443,fork,reuseaddr tcp-connect:$(CLUSTER_NAME)-control-plane:6443 > /dev/null; \
		fi; \
		KUBECONFIG=$$(mktemp); \
		trap 'rm -f "$$KUBECONFIG"' EXIT; \
		kind get kubeconfig --name "$(CLUSTER_NAME)" > "$$KUBECONFIG"; \
		sed -i 's/127.0.0.1:[0-9]*/127.0.0.1:16443/' "$$KUBECONFIG"; \
		mkdir -p "$$HOME/.kube"; \
		cp "$$KUBECONFIG" "$$HOME/.kube/config"; \
		echo "✓ kubeconfig patched to 127.0.0.1:16443"; \
	else \
		echo "→ Standard Docker — kubeconfig OK"; \
	fi

.PHONY: ingress-install
ingress-install: ## Install NGINX Ingress Controller (idempotent)
	@echo "→ Installing NGINX Ingress Controller..."
	@$(KUBECTL) apply -f \
		https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-$(INGRESS_NGINX_VERSION)/deploy/static/provider/kind/deploy.yaml
	@$(KUBECTL) label node --overwrite $(CLUSTER_NAME)-control-plane ingress-ready=true 2>/dev/null || true
	@echo "✓ Ingress controller installed"

.PHONY: ingress-wait
ingress-wait: ## Wait for ingress controller to be ready
	@echo "→ Waiting for ingress controller..."
	@$(KUBECTL) wait --namespace ingress-nginx \
		--for=condition=available deployment/ingress-nginx-controller \
		--timeout=180s
	@echo "✓ Ingress controller ready"

.PHONY: metrics-install
metrics-install: ## Install metrics-server for HPA autoscaling (idempotent)
	@echo "→ Installing metrics-server..."
	@$(KUBECTL) apply -f \
		https://github.com/kubernetes-sigs/metrics-server/releases/download/$(METRICS_SERVER_VERSION)/components.yaml
	@if $(KUBECTL) get deployment metrics-server -n kube-system \
		-o jsonpath='{.spec.template.spec.containers[0].args}' 2>/dev/null \
		| grep -q -- '--kubelet-insecure-tls'; then
		echo "→ metrics-server already has --kubelet-insecure-tls"
	else
		$(KUBECTL) patch deployment metrics-server -n kube-system --type=json \
			-p='[{"op": "add", "path": "/spec/template/spec/containers/0/args/-", "value": "--kubelet-insecure-tls"}]'
	fi
	@echo "✓ metrics-server installed"

.PHONY: metrics-wait
metrics-wait: ## Wait for metrics-server to be ready
	@echo "→ Waiting for metrics-server..."
	@$(KUBECTL) wait --namespace kube-system \
		--for=condition=available deployment/metrics-server \
		--timeout=180s
	@echo "✓ metrics-server ready"

.PHONY: storage-provisioner-install
storage-provisioner-install: ## Install local-path StorageClass (dynamic provisioning, idempotent)
	@echo "→ Installing local-path StorageClass..."
	@$(KUBECTL) apply -f \
		https://raw.githubusercontent.com/rancher/local-path-provisioner/$(LOCAL_PATH_PROVISIONER_VERSION)/deploy/local-path-storage.yaml
	@echo "✓ local-path StorageClass installed"

.PHONY: storage-provisioner-wait
storage-provisioner-wait: ## Wait for local-path-provisioner to be ready
	@echo "→ Waiting for local-path-provisioner..."
	@if $(KUBECTL) wait --namespace local-path-storage \
		--for=condition=available deployment/local-path-provisioner \
		--timeout=120s 2>/dev/null; then \
		echo "✓ local-path-provisioner ready"; \
	else \
		echo "  ⚠ local-path-provisioner not found (may use different namespace)"; \
	fi

.PHONY: tls-generate
tls-generate: namespace-ensure ## Generate TLS certificate for $(DOMAIN) (idempotent)
	@if $(KUBECTL) get secret nars-tls -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ TLS secret 'nars-tls' already exists"
	else
		echo "→ Generating TLS certificate for $(DOMAIN)..."
		TLS_TMPDIR=$$(mktemp -d); \
		trap 'rm -rf "$$TLS_TMPDIR"' EXIT; \
		CERT_FILE=$$TLS_TMPDIR/tls.crt; \
		KEY_FILE=$$TLS_TMPDIR/tls.key; \
		mkcert -cert-file "$$CERT_FILE" -key-file "$$KEY_FILE" "$(DOMAIN)"
		CERT_B64=$$(base64 -w0 < "$$CERT_FILE")
		KEY_B64=$$(base64 -w0 < "$$KEY_FILE")
		printf 'apiVersion: v1\nkind: Secret\nmetadata:\n  name: nars-tls\n  namespace: %s\ntype: kubernetes.io/tls\ndata:\n  tls.crt: %s\n  tls.key: %s\n' \
			"$(NAMESPACE)" "$$CERT_B64" "$$KEY_B64" \
		| $(KUBECTL) apply -f -
		echo "✓ TLS secret created"
	fi

.PHONY: ca-generate
ca-generate: ## Generate a self-signed CA certificate for mTLS (idempotent)
	@if [ -f "$(K8S_DIR)/certs/ca.crt" ]; then
		echo "→ CA cert already exists at $(K8S_DIR)/certs/ca.crt"
	else
		echo "→ Generating self-signed CA certificate..."
		mkdir -p "$(K8S_DIR)/certs"
		openssl ecparam -genkey -name prime256v1 -noout -out "$(K8S_DIR)/certs/ca.key"
		openssl req -x509 -new -nodes -key "$(K8S_DIR)/certs/ca.key" \
			-sha256 -days 3650 \
			-subj "/CN=nars-mtls-ca/O=NARS" \
			-out "$(K8S_DIR)/certs/ca.crt"
		chmod 600 "$(K8S_DIR)/certs/ca.key"
		echo "✓ CA cert generated:"
		echo "    cert: $(K8S_DIR)/certs/ca.crt"
		echo "    key:  $(K8S_DIR)/certs/ca.key"
		echo ""
		echo "  Issue client certs with:"
		echo "    openssl req -new -nodes -out client.csr \\"
		echo "      -subj '/CN=client-name/O=NARS'"
		echo "    openssl x509 -req -in client.csr \\"
		echo "      -CA $(K8S_DIR)/certs/ca.crt \\"
		echo "      -CAkey $(K8S_DIR)/certs/ca.key \\"
		echo "      -CAcreateserial -out client.crt -days 365 -sha256"
		echo "    rm client.csr"
	fi

.PHONY: ca-secret
ca-secret: ca-generate namespace-ensure ## Create mTLS CA secret from k8s/certs/ca.crt (idempotent)
	@if $(KUBECTL) get secret nars-ca -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ CA secret 'nars-ca' already exists"
	else
		echo "→ Creating mTLS CA secret..."
		CA_FILE="$(K8S_DIR)/certs/ca.crt"
		CA_B64=$$(base64 -w0 < "$$CA_FILE")
		printf 'apiVersion: v1\nkind: Secret\nmetadata:\n  name: nars-ca\n  namespace: %s\ndata:\n  ca.crt: %s\n' \
			"$(NAMESPACE)" "$$CA_B64" \
		| $(KUBECTL) apply -f -
		echo "✓ CA secret created"
	fi

.PHONY: secrets-validate
secrets-validate: ## Fail if kustomize output contains placeholder values (REPLACE_ME)
	@echo "→ Validating kustomize output for placeholder values..."
	@output=$$($(KUBECTL) kustomize "$(K8S_DIR)" 2>&1); \
	exit_code=$$?; \
	if [ "$$exit_code" -ne 0 ]; then \
		echo "✖ ERROR: kustomize failed (exit $$exit_code):"; \
		echo "$$output" | head -5; \
		exit 1; \
	fi; \
	if [ -z "$$output" ]; then \
		echo "✖ ERROR: kustomize produced empty output"; \
		exit 1; \
	fi; \
	if echo "$$output" | grep -q "REPLACE_ME"; then \
		echo "✖ ERROR: kustomize output contains REPLACE_ME placeholder values!"; \
		echo "  Run 'make secrets-apply' to generate real secrets from .env."; \
		echo "  Or edit the file directly to replace placeholders."; \
		echo ""; \
		echo "$$output" | grep -n "REPLACE_ME"; \
		exit 1; \
	fi; \
	echo "✓ No placeholder values found"

.PHONY: secrets-apply
secrets-apply: .env _check-secrets namespace-ensure ## Create nars-secrets and regcred with generated/variable values
# SECURITY: Uses temp files instead of --from-literal to avoid secret
# exposure in `ps aux` / CI logs. Files are cleaned up on exit.
	@echo "→ Creating 'nars-secrets'..."
	@tmpdir=$$(mktemp -d); \
	trap 'rm -rf "$$tmpdir"' EXIT; \
	printf '%s' "$$POSTGRES_PASSWORD" > "$$tmpdir/postgres_password"; \
	printf '%s' "Host=postgis;Port=5432;Database=nars_db;Username=postgres;Password=$$POSTGRES_PASSWORD" > "$$tmpdir/ConnectionStrings__DefaultConnection"; \
	printf '%s' "$$JWT_SECRET" > "$$tmpdir/Jwt__SecretKey"; \
	printf '%s' "$$GPG_PASSPHRASE" > "$$tmpdir/gpg-passphrase"; \
	$(KUBECTL) create secret generic nars-secrets -n "$(NAMESPACE)" \
		--from-file=postgres_password="$$tmpdir/postgres_password" \
		--from-file=ConnectionStrings__DefaultConnection="$$tmpdir/ConnectionStrings__DefaultConnection" \
		--from-file=Jwt__SecretKey="$$tmpdir/Jwt__SecretKey" \
		--from-file=gpg-passphrase="$$tmpdir/gpg-passphrase" \
		--dry-run=client -o yaml \
	| $(KUBECTL) apply -f -
	@echo "✓ nars-secrets created"

	@if [ -n "$(DOCKER_TOKEN)" ]; then \
		echo "→ Creating 'regcred'..."; \
		TMPDIR_REGCRED=$$(mktemp -d); \
		trap 'rm -rf "$$TMPDIR_REGCRED"' EXIT; \
		printf '%s' "$$DOCKER_TOKEN" > "$$TMPDIR_REGCRED/docker_password"; \
		printf '%s' "$$DOCKER_USERNAME" > "$$TMPDIR_REGCRED/docker_username"; \
		$(KUBECTL) create secret docker-registry regcred -n "$(NAMESPACE)" \
			--docker-server=https://index.docker.io/v1/ \
			--docker-username-file="$$TMPDIR_REGCRED/docker_username" \
			--docker-password-file="$$TMPDIR_REGCRED/docker_password" \
			--dry-run=client -o yaml \
		| $(KUBECTL) apply -f -; \
		echo "✓ regcred created"; \
	else \
		echo "→ Skipping regcred (DOCKER_TOKEN not set — using locally loaded images)"; \
	fi

.PHONY: kustomize-set-image-tag
kustomize-set-image-tag: ## Pin all kustomize image tags to IMAGE_TAG (e.g. IMAGE_TAG=abc1234)
	@if [ -z "$(IMAGE_TAG)" ]; then \
		echo "✖ IMAGE_TAG is empty — specify a tag (e.g. IMAGE_TAG=abc1234)"; \
		exit 1; \
	fi
	@if echo "$(IMAGE_TAG)" | grep -qE '[^a-zA-Z0-9._-]'; then \
		echo "✖ IMAGE_TAG='$(IMAGE_TAG)' contains invalid characters (only alphanumeric, dots, hyphens, underscores allowed)"; \
		exit 1; \
	fi
	@if echo "$(IMAGE_TAG)" | grep -qi "^latest$$"; then \
		echo "  ⚠ IMAGE_TAG=$(IMAGE_TAG) is 'latest' — not pinning. Set IMAGE_TAG=<commit-sha> for reproducible deployments."; \
	else \
		echo "→ Pinning kustomize image tags to $(IMAGE_TAG)..."; \
		(cd "$(K8S_DIR)" && \
		kustomize edit set image \
			$(DOCKER_ORG)/nars-api=$(DOCKER_ORG)/nars-api:$(IMAGE_TAG) \
			$(DOCKER_ORG)/nars-postgis=$(DOCKER_ORG)/nars-postgis:$(IMAGE_TAG) \
			$(DOCKER_ORG)/nars-vite=$(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)); \
		echo "✓ Image tags pinned to $(IMAGE_TAG)"; \
	fi

.PHONY: kustomize-apply
kustomize-apply: secrets-validate ## Apply k8s manifests via kustomize (pin tags with IMAGE_TAG=<sha>)
	$(MAKE) kustomize-set-image-tag
	$(MAKE) postgis-pv-fix
	@echo "→ Applying kustomization..."
	@$(KUBECTL) kustomize "$(K8S_DIR)" | $(KUBECTL) apply -f -
	@echo "✓ Kustomization applied"

	@echo "→ Waiting for postgis..."
	@if $(KUBECTL) get deployment postgis -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		$(KUBECTL) wait --namespace "$(NAMESPACE)" \
			--for=condition=Available deployment/postgis --timeout=240s
		$(MAKE) postgis-password-sync
		$(MAKE) postgis-migration-baseline
	fi

	@echo "→ Waiting for app deployments..."
	@for deploy in $(filter-out postgis,$(SCALABLE_DEPLOYS)); do
		if $(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
			if ! $(KUBECTL) wait --namespace "$(NAMESPACE)" \
				--for=condition=Available "deployment/$$deploy" --timeout=240s; then
				echo "✖ Deployment '$$deploy' did not become Available in time."
				echo "→ describe deployment/$$deploy"
				$(KUBECTL) describe deployment "$$deploy" -n "$(NAMESPACE)" || true
				echo "→ pods for $$deploy"
				$(KUBECTL) get pods -n "$(NAMESPACE)" -l "app.kubernetes.io/name=$$deploy" -o wide || true
				for pod in $$($(KUBECTL) get pods -n "$(NAMESPACE)" -l "app.kubernetes.io/name=$$deploy" -o name); do
					echo "→ logs $$pod (current)"
					$(KUBECTL) logs -n "$(NAMESPACE)" $$pod --tail=120 || true
					echo "→ logs $$pod (previous)"
					$(KUBECTL) logs -n "$(NAMESPACE)" $$pod --previous --tail=120 || true
				done
				exit 1
			fi
		fi
	done
	@echo "✓ All deployments ready"

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
# Make expands $$$$ → $$, then the single-quoted heredoc 'SQL' prevents
# shell expansion, so psql receives $$ as PL/pgSQL dollar-quoting.
	@cat <<'SQL' | $(KUBECTL) exec -i -n "$(NAMESPACE)" deployment/postgis -- \
		psql -U postgres -d nars_db -v ON_ERROR_STOP=1 >/dev/null
	CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
	    "MigrationId" character varying(150) NOT NULL,
	    "ProductVersion" character varying(32) NOT NULL,
	    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
	);

	DO $$$$
	DECLARE
	    has_areas boolean;
	    has_inspections boolean;
	BEGIN
	    SELECT EXISTS (
	        SELECT 1
	        FROM information_schema.tables
	        WHERE table_schema = 'public' AND table_name = 'areas'
	    ) INTO has_areas;

	    SELECT EXISTS (
	        SELECT 1
	        FROM information_schema.tables
	        WHERE table_schema = 'public' AND table_name = 'inspections'
	    ) INTO has_inspections;

	    IF has_areas THEN
	        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
	        VALUES ('20260510191030_AddErrorLogs', '10.0.7')
	        ON CONFLICT ("MigrationId") DO NOTHING;
	    END IF;

	    IF has_inspections THEN
	        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
	        VALUES ('20260511062948_AddInspections', '10.0.7')
	        ON CONFLICT ("MigrationId") DO NOTHING;
	    END IF;

	    IF has_areas AND has_inspections THEN
	        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
	        VALUES ('20260705061915_MigrateToTimestamptz', '10.0.7')
	        ON CONFLICT ("MigrationId") DO NOTHING;
	    END IF;
	END $$$$;
	SQL
	@echo "✓ EF migration history baseline ensured"

.PHONY: postgis-pv-fix
postgis-pv-fix: ## Fix postgis PV permissions inside kind container (rootless Docker workaround)
	@echo "→ Fixing postgis PV permissions..."
	@if docker exec $(CLUSTER_NAME)-control-plane sh -c ' \
		if [ -d /mnt/nars/postgis/data ]; then \
			chown -R 999:999 /mnt/nars/postgis/data; \
			chmod 700 /mnt/nars/postgis/data; \
			echo "✓ postgis data dir ownership set to 999:999"; \
		else \
			echo "⚠  /mnt/nars/postgis/data not found inside kind (PV not mounted yet?)"; \
			exit 1; \
		fi \
	' 2>/dev/null; then \
		echo "✓ Postgis PV permission fix applied"; \
	else \
		echo "⚠  Could not chown postgis data dir (non-fatal)"; \
	fi

# ─── Observability (Grafana LGTM + OTel) ─────────────────────

.PHONY: observability-install
observability-install: helm-check helm-repos ## Install LGTM stack + OpenTelemetry Collector
	$(MAKE) observability-namespace
	$(MAKE) observability-prometheus-stack
	$(MAKE) observability-loki
	$(MAKE) observability-tempo
	$(MAKE) observability-otel-collector
	$(MAKE) observability-servicemonitor
	@echo ""
	@echo "✓ Observability stack installed!"
	@echo ""
	@echo "  Port-forward:  make observability-port-forward"
	@echo "  Grafana:       http://localhost:3000 (credentials in local password manager)"
	@echo "  Loki:          http://localhost:3100"
	@echo "  Tempo:         http://localhost:3200"

.PHONY: helm-check
helm-check: ## Check that helm is installed
	@command -v helm >/dev/null 2>&1 || { echo "✖ helm is not installed"; exit 1; }

.PHONY: helm-repos
helm-repos: ## Ensure required Helm chart repos are configured
	@echo "→ Configuring Helm repositories..."
	@helm repo add prometheus-community https://prometheus-community.github.io/helm-charts --force-update
	@helm repo add grafana https://grafana.github.io/helm-charts --force-update
	@helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts --force-update
	@helm repo update
	@echo "✓ Helm repositories ready"

.PHONY: observability-namespace
observability-namespace: ## Ensure observability namespace exists (idempotent)
	@$(KUBECTL) create namespace $(OBSERVABILITY_NAMESPACE) --dry-run=client -o yaml | $(KUBECTL) apply -f -

.PHONY: observability-prometheus-stack
observability-prometheus-stack: ## Install Prometheus + Grafana + AlertManager
	@echo "→ Installing kube-prometheus-stack..."
	@tmpdir=$$(mktemp -d); \
	trap 'rm -rf "$$tmpdir"' EXIT; \
	printf '%s' "$$GRAFANA_PASSWORD" > "$$tmpdir/grafana_password"; \
	helm upgrade --install prometheus-stack prometheus-community/kube-prometheus-stack \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--set-file grafana.adminPassword="$$tmpdir/grafana_password" \
		--set grafana.service.type=ClusterIP \
		--set grafana.service.port=3000 \
		--set grafana.additionalDataSources[0].name=Loki \
		--set grafana.additionalDataSources[0].type=loki \
		--set grafana.additionalDataSources[0].url=http://loki-gateway:80 \
		--set grafana.additionalDataSources[0].access=proxy \
		--set grafana.additionalDataSources[1].name=Tempo \
		--set grafana.additionalDataSources[1].type=tempo \
		--set grafana.additionalDataSources[1].url=http://tempo:3200 \
		--set grafana.additionalDataSources[1].access=proxy \
		--set grafana.additionalDataSources[1].jsonData.tracesToLogsV2.datasourceUid=loki \
		--set grafana.additionalDataSources[1].jsonData.lokiSearch.datasourceUid=loki \
		--set prometheus.prometheusSpec.resources.requests.cpu=100m \
		--set prometheus.prometheusSpec.resources.requests.memory=256Mi \
		--set prometheus.prometheusSpec.resources.limits.cpu=500m \
		--set prometheus.prometheusSpec.resources.limits.memory=1Gi \
		--set alertmanager.enabled=false \
		--set defaultRules.create=false \
		--set nodeExporter.enabled=false \
		--set kubeStateMetrics.enabled=false \
		--timeout 10m

.PHONY: observability-loki
observability-loki: ## Install Loki (logs)
	@echo "→ Installing Loki..."
	@helm upgrade --install loki grafana/loki \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--set deploymentMode=SingleBinary \
		--set loki.commonConfig.replication_factor=1 \
		--set loki.auth_enabled=false \
		--set loki.storage.type=filesystem \
		--set loki.storage.bucketNames.chunks=loki-chunks \
		--set loki.storage.bucketNames.admin=loki-admin \
		--set loki.useTestSchema=true \
		--set loki.ruler.enabled=false \
		--set singleBinary.replicas=1 \
		--set singleBinary.persistence.volumeClaimsEnabled=false \
		--set singleBinary.resources.requests.cpu=50m \
		--set singleBinary.resources.requests.memory=128Mi \
		--set singleBinary.resources.limits.cpu=200m \
		--set singleBinary.resources.limits.memory=512Mi \
		--set write.replicas=0 \
		--set read.replicas=0 \
		--set backend.replicas=0 \
		--set chunksCache.enabled=false \
		--set resultsCache.enabled=false \
		--set test.enabled=false \
		--set monitoring.lokiCanary.enabled=false \
		--set monitoring.selfMonitoring.enabled=false \
		--timeout 10m

.PHONY: observability-tempo
observability-tempo: ## Install Tempo (traces)
	@echo "→ Installing Tempo..."
	@helm upgrade --install tempo grafana/tempo \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--set tempo.replicas=1 \
		--set tempo.resources.requests.cpu=50m \
		--set tempo.resources.requests.memory=128Mi \
		--set tempo.resources.limits.cpu=200m \
		--set tempo.resources.limits.memory=512Mi \
		--set tempo.storage.trace.backend=local \
		--set tempo.storage.trace.local.path=/var/tempo/traces \
		--set tempo.storage.trace.wal.path=/var/tempo/wal \
		--set tempo.ingester.max_block_duration=5m \
		--set tempo.readinessProbe.initialDelaySeconds=60 \
		--set tempo.readinessProbe.failureThreshold=10 \
		--set tempo.livenessProbe.initialDelaySeconds=60 \
		--set tempo.livenessProbe.failureThreshold=10 \
		--set memBallastSizeMbs=128 \
		--set test.enabled=false \
		--timeout 10m

.PHONY: observability-otel-collector
observability-otel-collector: ## Install OpenTelemetry Collector
	@echo "→ Installing OpenTelemetry Collector..."
	@helm upgrade --install otel-collector open-telemetry/opentelemetry-collector \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--values $(K8S_DIR)/helm-values/opentelemetry-collector.yaml \
		--timeout 10m

.PHONY: observability-servicemonitor
observability-servicemonitor: ## Apply OTel metrics Service + ServiceMonitor (requires prometheus CRDs)
	@echo "→ Applying OTel metrics Service and ServiceMonitor..."
	@$(KUBECTL) apply -f - < $(K8S_DIR)/otel-metrics-service.yaml
	@$(KUBECTL) apply -f - < $(K8S_DIR)/servicemonitor.yaml
	@echo "✓ OTel metrics Service + ServiceMonitor applied"

.PHONY: observability-port-forward
observability-port-forward: ## Port-forward Grafana, Loki, Tempo (background)
	@mkdir -p "$(LOG_DIR)"; \
	echo "→ Starting observability port-forwards (background)..."; \
	pkill -f "port-forward.*observability.*grafana" 2>/dev/null || true; \
	pkill -f "port-forward.*observability.*loki-gateway" 2>/dev/null || true; \
	pkill -f "port-forward.*observability.*tempo" 2>/dev/null || true; \
	sleep 0.5; \
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/prometheus-stack-grafana 3000:3000 \
		> "$(LOG_DIR)/port-forward-grafana.log" 2>&1 & \
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/loki-gateway 3100:80 \
		> "$(LOG_DIR)/port-forward-loki.log" 2>&1 & \
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/tempo 3200:3200 \
		> "$(LOG_DIR)/port-forward-tempo.log" 2>&1 & \
	echo "✓ Port-forwards running"; \
	echo "  Grafana: http://localhost:3000 (run \`make grafana-password\` to retrieve credentials)"; \
	echo "  Loki:    http://localhost:3100"; \
	echo "  Tempo:   http://localhost:3200"; \
	echo "  Logs:    tail -f $(LOG_DIR)/port-forward-{grafana,loki,tempo}.log"

.PHONY: observability-stop
observability-stop: ## Stop observability port-forwards
	@pkill -f "port-forward.*observability" 2>/dev/null || true
	@echo "✓ Port-forwards stopped"

.PHONY: grafana-password
grafana-password: ## Show the generated Grafana admin password (stderr only, non-CI safe)
	@if [ -t 2 ]; then \
		echo "$(GRAFANA_PASSWORD)" >&2; \
	else \
		echo "⚠ Refusing to print password to non-tty stderr (use 'make grafana-password' interactively)" >&2; \
		exit 1; \
	fi

# ─── Code Quality (nars-infra) ──────────────────────────────

.PHONY: infra-lint
infra-lint: ## Run all nars-infra linters (shell, docker, yaml)
	$(MAKE) infra-lint-shell
	$(MAKE) infra-lint-docker
	$(MAKE) infra-lint-yaml
	$(MAKE) infra-lint-makefile

.PHONY: infra-lint-shell
infra-lint-shell: ## Shell-check nars-infra/scripts/*.sh
	@if command -v shellcheck >/dev/null 2>&1; then
		shellcheck nars-infra/scripts/*.sh
	else
		docker run --rm -v "$$(pwd):/mnt" koalaman/shellcheck:stable \
			/mnt/nars-infra/scripts/*.sh
	fi

.PHONY: infra-lint-docker
infra-lint-docker: ## Lint Dockerfiles with hadolint
	@if command -v hadolint >/dev/null 2>&1; then
		hadolint --failure-threshold error nars-infra/docker/Dockerfile.*
	else
		docker run --rm \
			-v "$$(pwd):/mnt" \
			-v "$$(pwd)/nars-infra/.hadolint.yaml:/home/hadolint/.hadolint.yaml:ro" \
			hadolint/hadolint \
			hadolint --failure-threshold error /mnt/nars-infra/docker/Dockerfile.*
	fi

.PHONY: infra-lint-yaml
infra-lint-yaml: ## Lint k8s YAML with yamllint (uses .yamllint.yaml config)
	@if command -v yamllint >/dev/null 2>&1; then
		yamllint -c nars-infra/.yamllint.yaml nars-infra/k8s/*.yaml nars-infra/k8s/helm-values/*.yaml
	else
		docker run --rm -v "$$(pwd):/mnt" $(YAMLLINT_IMAGE) \
			-c /mnt/nars-infra/.yamllint.yaml /mnt/nars-infra/k8s/*.yaml /mnt/nars-infra/k8s/helm-values/*.yaml
	fi

.PHONY: infra-lint-makefile
infra-lint-makefile: ## Validate Makefile syntax with dry-run
	@echo "→ Checking Makefile syntax..."
	@make -n help > /dev/null 2>&1 && echo "✓ Makefile syntax OK" \
		|| { echo "✖ Makefile syntax error"; exit 1; }
	@echo "→ Checking undefined variable references..."
	@make -Rr --warn-undefined-variables -n help 2>&1 | grep -i 'warning.*undefined' || true

# ─── Docker Images ───────────────────────────────────────────

# Override IMAGE_TAG to pin a specific version (e.g., IMAGE_TAG=abc1234).
# Defaults to 'latest' for local dev. CI/CD should set this to the commit SHA.
IMAGE_TAG ?= latest

.PHONY: images-build
images-build: ## Build all Docker images
	@if echo "$(IMAGE_TAG)" | grep -qi "^latest$$"; then \
		echo "  ⚠ IMAGE_TAG=latest — set IMAGE_TAG=<commit-sha> for CI/CD builds"; \
	fi
	@echo "→ Building images..."
	$(MAKE) _build-nars-api
	$(MAKE) _build-nars-postgis
	$(MAKE) _build-nars-vite
	$(MAKE) _build-nars-backup
	@echo "✓ All images built"

.PHONY: _build-nars-api
_build-nars-api:
	@echo "  → $(DOCKER_ORG)/nars-api:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-api" \
		-t "$(DOCKER_ORG)/nars-api:$(IMAGE_TAG)" .

.PHONY: _build-nars-postgis
_build-nars-postgis:
	@echo "  → $(DOCKER_ORG)/nars-postgis:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-postgis" \
		-t "$(DOCKER_ORG)/nars-postgis:$(IMAGE_TAG)" .

.PHONY: _build-nars-vite
_build-nars-vite:
	@echo "  → $(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-vite" \
		-t "$(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)" .

.PHONY: _build-nars-backup
_build-nars-backup:
	@echo "  → $(DOCKER_ORG)/nars-backup:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-backup" \
		-t "$(DOCKER_ORG)/nars-backup:$(IMAGE_TAG)" .

.PHONY: images-push
images-push: ## Push all Docker images to registry
	@if echo "$(IMAGE_TAG)" | grep -qi "^latest$$"; then \
		echo "  ⚠ Pushing 'latest' tag — set IMAGE_TAG=<commit-sha> for CI/CD builds"; \
	fi
	@	for img in $(REGISTRY_IMAGES); do
		echo "→ Pushing $(DOCKER_ORG)/$$img:$(IMAGE_TAG)..."
		docker push "$(DOCKER_ORG)/$$img:$(IMAGE_TAG)"
	done
	@echo "✓ All images pushed"

.PHONY: images-load
images-load: ## Load locally built Docker images into the kind cluster
	@if echo "$(IMAGE_TAG)" | grep -qi "^latest$$"; then \
		echo "  ⚠ Loading 'latest' tag — set IMAGE_TAG=<commit-sha> for CI/CD builds"; \
	fi
	@for img in $(REGISTRY_IMAGES); do
		full="$(DOCKER_ORG)/$$img:$(IMAGE_TAG)"
		if docker image inspect "$$full" >/dev/null 2>&1; then
			echo "→ Loading $$full into cluster..."
			kind load docker-image "$$full" --name "$(CLUSTER_NAME)"
		else
			echo "  ⚠ $$full not found locally — will pull from registry"
		fi
	done
	@echo "✓ Images loaded"

.PHONY: frontend-update
frontend-update: ## Rebuild nars-vite, load into kind, and rollout restart
	$(MAKE) _build-nars-vite
	@kind load docker-image "$(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)" --name "$(CLUSTER_NAME)"
	@$(KUBECTL) rollout restart deployment nars-frontend -n "$(NAMESPACE)"
	@$(KUBECTL) rollout status deployment nars-frontend -n "$(NAMESPACE)" --timeout=120s
	@echo "✓ nars-vite rebuilt and deployed"
