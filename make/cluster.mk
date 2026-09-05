# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: cluster lifecycle.


# ─── Prerequisites ──────────────────────────────────────────

.PHONY: prerequisites
prerequisites: ## Check that all required tools are installed
	@echo "→ Checking prerequisites..."
	@command -v kind >/dev/null 2>&1 || { echo "✖ kind is not installed → https://kind.sigs.k8s.io/docs/user/quick-start/"; exit 1; }
	@command -v kubectl >/dev/null 2>&1 || { echo "✖ kubectl is not installed"; exit 1; }
	@command -v mkcert >/dev/null 2>&1 || { echo "✖ mkcert is not installed → https://github.com/FiloSottile/mkcert"; exit 1; }
	@command -v docker >/dev/null 2>&1 || { echo "✖ docker is not installed"; exit 1; }
	@echo "✓ All prerequisites met"


# ─── Cluster Lifecycle ──────────────────────────────────────

.PHONY: cluster-up
cluster-up: prerequisites _check-secrets ## Full bootstrap: create cluster, build images, deploy everything
	$(SUBMAKE) cluster-create
	$(SUBMAKE) kubeconfig-fix
	$(SUBMAKE) ingress-install
	$(SUBMAKE) ingress-wait
	$(SUBMAKE) metrics-install
	$(SUBMAKE) metrics-wait
	$(SUBMAKE) storage-provisioner-install
	$(SUBMAKE) storage-provisioner-wait
	$(SUBMAKE) images-build
	$(SUBMAKE) tls-generate
	$(SUBMAKE) ca-secret
	$(SUBMAKE) secrets-apply
	$(SUBMAKE) images-load
	$(SUBMAKE) kustomize-apply
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
	$(SUBMAKE) cluster-up
	$(SUBMAKE) observability-install
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

# Shared pg_dump + gzip logic.
# REQUIRED shell variables (must be set before expansion):
#   $$POD  — postgis pod name (from POSTGIS_GET_POD_CMD)
#   $$PASS — postgres password (from k8s secret)
#   $$PREFIX (optional) — disambiguates the output filename so two backups taken in
# the same second can never overwrite each other (manual_ vs auto_). The
# nanosecond timestamp (%N) additionally makes same-second runs of the same
# target unique, so `make db-backup` twice in a row can never clobber an
# earlier dump.
# SECURITY: Password piped via stdin to avoid exposure in container's process table.
_pg_dump_cmd = \
	mkdir -p "$(BACKUP_DIR)"; \
	TIMESTAMP=$$(date +"%Y%m%d_%H%M%S_%N"); \
	FILE="$(BACKUP_DIR)/$${PREFIX:+$${PREFIX}_}nars_db_$${TIMESTAMP}.sql"; \
	printf '%s\n' "$$PASS" | \
		$(KUBECTL) exec -i "$$POD" -n "$(NAMESPACE)" -- \
		bash -c 'read -r _pw; PGPASSWORD="$$_pw" pg_dump -U postgres -d "$(DB_NAME)" --no-owner' > "$$FILE"; \
	gzip -f "$$FILE"

# Internal: auto-backup before cluster teardown. Blocks if backup fails.
.PHONY: _pre-cluster-down-backup
_pre-cluster-down-backup:
	@POD=$$($(POSTGIS_GET_POD_CMD) || true);
	if [ -z "$$POD" ]; then
		echo "→ No postgis pod running — skipping auto-backup";
		exit 0;
	fi;
	echo "→ Auto-backing up database before cluster teardown...";
	PASS=$$($(_get_db_password_cmd));
	if [ -z "$$PASS" ]; then
		echo "  ⚠ Could not read DB password — skipping backup";
		exit 0;
	fi;
	PREFIX=auto;
	$(_pg_dump_cmd);
	if [ ! -s "$${FILE}.gz" ]; then
		echo "✖ Auto-backup FAILED — refusing to tear down cluster";
		rm -f "$${FILE}.gz";
		exit 1;
	fi;
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
	@if [ -t 0 ]; then
		read -p "  Type the cluster name '$(CLUSTER_NAME)' to confirm: " confirm;
		if [ "$$confirm" != "$(CLUSTER_NAME)" ]; then echo "  Cancelled."; exit 1; fi;
	else
		echo "  Non-interactive shell — refusing destructive operation.";
		exit 1;
	fi
	$(SUBMAKE) cluster-down
	@echo "→ Wiping postgis data..."
	@if rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null; then
		echo "✓ Data wiped";
	elif sudo -n rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null; then
		echo "✓ Data wiped (via sudo)";
	else
		echo "✖ Failed to remove $(POSTGRES_DATA_DIR) — check permissions";
		exit 1;
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
cluster-port-forward: ## Deprecated — use 'proxy-up' directly.
	@echo "⚠ DEPRECATED: 'cluster-port-forward' is deprecated — use 'proxy-up' instead." >&2
	@$(SUBMAKE) proxy-up


.PHONY: cluster-stop
cluster-stop: ## Scale all deployments to 0 (stop pods, keep cluster)
	@echo "→ Stopping all pods..."
	@mkdir -p "$(BACKUP_DIR)/replicas"
	@for deploy in $(SCALABLE_DEPLOYS); do
		if ! $(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" >/dev/null 2>&1; then
			echo "  ⚠ $$deploy not found, skipping"
			continue
		fi
		saved="$(BACKUP_DIR)/replicas/$$deploy.txt"
		replicas=$$($(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" \
			-o jsonpath='{.spec.replicas}' 2>/dev/null || echo "1")
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
		KIND_CFG=$$(mktemp /tmp/kind-$(CLUSTER_NAME)-XXXXXX.yaml);
		trap 'rm -f "$$KIND_CFG"' EXIT;
		{
			echo 'kind: Cluster';
			echo 'apiVersion: kind.x-k8s.io/v1alpha4';
			echo "name: $(CLUSTER_NAME)";
			echo 'kubeadmConfigPatches:';
			echo '  - |';
			echo '    kind: ClusterConfiguration';
			echo '    apiServer:';
			echo '      certSANs:';
			echo '        - localhost';
			echo '        - 127.0.0.1';
			echo '        - 0.0.0.0';
			echo 'nodes:';
			echo '  - role: control-plane';
			echo '    extraMounts:';
			echo "      - hostPath: $$DATA_DIR";
			echo '        containerPath: /mnt/nars/postgis';
		} > "$$KIND_CFG"
		echo "→ Creating kind cluster '$(CLUSTER_NAME)'..."
		kind create cluster --name "$(CLUSTER_NAME)" --config "$$KIND_CFG"
		echo "✓ Cluster created"
	fi
	$(SUBMAKE) cluster-wait

.PHONY: cluster-wait
cluster-wait: ## Wait for API server and nodes to be ready
	@echo "→ Waiting for API server and nodes..."
	@if ! $(KUBECTL) get nodes >/dev/null 2>&1; then
		if docker info 2>/dev/null | grep -q "rootless"; then
			echo "→ Rootless Docker detected — fixing kubeconfig first";
			$(SUBMAKE) kubeconfig-fix;
		fi;
		i=0; until $(KUBECTL) get nodes 2>/dev/null; do
			sleep 2; i=$$((i + 1));
			[ "$$i" -ge 60 ] && { echo "Timed out waiting for nodes"; exit 1; };
		done;
	fi;
	$(KUBECTL) wait --for=condition=Ready node --all --timeout=120s
	@echo "✓ Cluster ready"

.PHONY: kubeconfig-fix
kubeconfig-fix: ## Patch kubeconfig for rootless Docker (port 16443 via kube-proxy socat)
	@if docker info 2>/dev/null | grep -q "rootless"; then
		echo "→ Rootless Docker detected — fixing kubeconfig port";
		KUBE_PROXY="kube-proxy";
		if ! docker ps --format "{{.Names}}" 2>/dev/null | grep -q "^$$KUBE_PROXY$$"; then
		echo "→ Creating socat bridge (16443 -> $(CLUSTER_NAME)-control-plane:6443)...";
		if ! docker run -d --name "$$KUBE_PROXY" --rm \
			-p 127.0.0.1:16443:16443 \
			--network kind \
			$(SOCAT_IMAGE) \
			tcp-listen:16443,fork,reuseaddr tcp-connect:$(CLUSTER_NAME)-control-plane:6443 > /dev/null; then
			echo "✖ Failed to create socat bridge container";
			exit 1;
		fi;
		fi;
		KUBECONFIG=$$(mktemp);
		trap 'rm -f "$$KUBECONFIG"' EXIT;
		kind get kubeconfig --name "$(CLUSTER_NAME)" > "$$KUBECONFIG";
		sed -i 's/127.0.0.1:[0-9]*/127.0.0.1:16443/' "$$KUBECONFIG";
		mkdir -p "$$HOME/.kube";
		if [ -f "$$HOME/.kube/config" ] && ! cmp -s "$$HOME/.kube/config" "$$KUBECONFIG"; then
			cp "$$HOME/.kube/config" "$$HOME/.kube/config.bak.$$(date +%Y%m%d_%H%M%S)";
			echo "  → Existing kubeconfig backed up before overwrite";
		fi;
		cp "$$KUBECONFIG" "$$HOME/.kube/config";
		echo "✓ kubeconfig patched to 127.0.0.1:16443";
	else
		echo "→ Standard Docker — kubeconfig OK";
	fi


.PHONY: postgis-pv-fix
postgis-pv-fix: ## Fix postgis PV permissions inside kind container (rootless Docker workaround)
	@echo "→ Fixing postgis PV permissions..."
	@if docker exec "$(CLUSTER_NAME)-control-plane" sh -c ' \
		if [ -d /mnt/nars/postgis/data ]; then
			chown -R 999:999 /mnt/nars/postgis/data;
			chmod 700 /mnt/nars/postgis/data;
			echo "✓ postgis data dir ownership set to 999:999";
		else
			echo "⚠  /mnt/nars/postgis/data not found inside kind (PV not mounted yet?)";
			exit 1;
		fi
	' 2>/dev/null; then
		echo "✓ Postgis PV permission fix applied";
	else
		echo "⚠  Could not chown postgis data dir (non-fatal)";
	fi


.PHONY: clean
clean: ## NOT a build-clean — refuses accidental cluster teardown (use cluster-down / cluster-clean)
	@echo "⚠ 'make clean' tears down the cluster — this is NOT a build-clean."
	@echo "  Use 'make cluster-down' (data preserved) or 'make cluster-clean' (data wiped)."
	@if [ -t 0 ]; then
		read -p "  Type '$(CLUSTER_NAME)' to proceed with teardown: " confirm
		if [ "$$confirm" != "$(CLUSTER_NAME)" ]; then echo "  Cancelled."; exit 1; fi
	else
		echo "  Non-interactive shell — refusing teardown."
		exit 1
	fi
	@$(SUBMAKE) cluster-down
