SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.ONESHELL:
.DEFAULT_GOAL := help

CLUSTER_NAME       ?= nars
NAMESPACE          ?= nars
DOMAIN             ?= nars.dz
K8S_DIR            ?= k8s
DOCKER_DIR         ?= Docker
DOCKER_ORG         ?= zuhirbenslama
DOCKER_USERNAME    ?= zuhirbenslama
DOCKER_TOKEN       ?=
POSTGRES_PASSWORD  ?= $(shell openssl rand -base64 32)
JWT_SECRET         ?= $(shell openssl rand -base64 32)
BACKUP_DIR         ?= $(POSTGRES_DATA_DIR)/backups
DB_NAME            ?= nars_db
POSTGRES_DATA_DIR  ?= data/nars/postgres
REGISTRY_IMAGES    := nars-api nars-postgres nars-vite
SCALABLE_DEPLOYS   := postgres nars-api nars-frontend

-include .env
export

.PHONY: help
help:
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

# ─── Cluster Lifecycle ──────────────────────────────────────

.PHONY: cluster-up
cluster-up: prerequisites ## Full bootstrap: create cluster, deploy everything
	$(MAKE) cluster-create
	$(MAKE) ingress-install
	$(MAKE) ingress-wait
	$(MAKE) tls-generate
	$(MAKE) ca-secret
	$(MAKE) secrets-apply
	$(MAKE) images-load
	$(MAKE) kustomize-apply
	@echo ""
	@echo "✓ Cluster '$(CLUSTER_NAME)' is ready!"
	@echo ""
	@echo "  Port-forward:  make cluster-port-forward"
	@echo "  Visit:         http://$(DOMAIN):8080/"
	@echo "  Health:        http://$(DOMAIN):8080/health"
	@echo "  Stop pods:     make cluster-stop"
	@echo "  Tear down:     make cluster-down (data preserved)"
	@echo "  Destroy data:  make cluster-clean"

.PHONY: cluster-down
cluster-down: ## Delete the kind cluster (preserves postgres data)
	@echo "→ Deleting cluster '$(CLUSTER_NAME)'..."
	@kind delete cluster --name "$(CLUSTER_NAME)" 2>/dev/null || true
	@rm -f /tmp/$(CLUSTER_NAME)-tls.crt /tmp/$(CLUSTER_NAME)-tls.key
	@echo "✓ Cluster deleted (postgres data preserved at $(POSTGRES_DATA_DIR))"

.PHONY: cluster-rebuild
cluster-rebuild: cluster-down cluster-up ## Delete and recreate the cluster

.PHONY: cluster-clean
cluster-clean: ## Delete cluster AND wipe postgres data (irreversible!)
	@echo "⚠  WARNING: This will DESTROY all postgres data at $(POSTGRES_DATA_DIR)"
	@read -p "  Type the cluster name '$(CLUSTER_NAME)' to confirm: " confirm; \
		if [ "$$confirm" != "$(CLUSTER_NAME)" ]; then echo "  Cancelled."; exit 0; fi
	$(MAKE) cluster-down
	@echo "→ Wiping postgres data..."
	@if echo "$(POSTGRES_DATA_DIR)" | grep -q '^/'; then
		sudo rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null || rm -rf "$(POSTGRES_DATA_DIR)" 2>/dev/null || true
	else
		rm -rf "$(POSTGRES_DATA_DIR)"
	fi
	@echo "✓ Data wiped"

.PHONY: cluster-status
cluster-status: ## Show cluster resources
	@echo "=== Nodes ==="
	@kubectl get nodes 2>/dev/null || echo "(cluster not running)"
	@echo ""
	@echo "=== Namespace: $(NAMESPACE) ==="
	@kubectl get all,ingress,pvc -n "$(NAMESPACE)" 2>/dev/null || echo "(not deployed)"
	@echo ""
	@echo "=== Endpoints ==="
	@kubectl get endpoints -n "$(NAMESPACE)" 2>/dev/null || true

.PHONY: cluster-logs
cluster-logs: ## Tail logs from all pods in the namespace
	@kubectl logs -n "$(NAMESPACE)" --all-containers --tail=50 --follow 2>/dev/null \
		|| echo "No pods found in namespace '$(NAMESPACE)'"

.PHONY: cluster-port-forward
cluster-port-forward: ## Port-forward ingress controller to localhost (background)
	@echo "→ Starting port-forward (background)..."
	@-pkill -f "kubectl port-forward.*ingress-nginx" 2>/dev/null || true
	@sleep 0.5
	@nohup kubectl port-forward -n ingress-nginx \
		service/ingress-nginx-controller \
		8080:80 8443:443 \
		> /tmp/port-forward-$(CLUSTER_NAME).log 2>&1 &
	@echo "✓ Port-forward running (PID: $$!)"
	@echo "  HTTP:  http://$(DOMAIN):8080/"
	@echo "  HTTPS: https://$(DOMAIN):8443/"
	@echo "  Logs:  tail -f /tmp/port-forward-$(CLUSTER_NAME).log"

# ─── Stop / Start (scale to 0/1) ────────────────────────────

.PHONY: cluster-stop
cluster-stop: ## Scale all deployments to 0 (stop pods, keep cluster)
	@echo "→ Stopping all pods..."
	@for deploy in $(SCALABLE_DEPLOYS); do
		saved="$(BACKUP_DIR)/replicas/$$deploy.txt"
		replicas=$$(kubectl get deployment $$deploy -n "$(NAMESPACE)" \
			-o jsonpath='{.spec.replicas}' 2>/dev/null || echo "1")
		mkdir -p "$(BACKUP_DIR)/replicas"
		echo "$$replicas" > "$$saved"
		kubectl scale deployment $$deploy -n "$(NAMESPACE)" --replicas=0
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
		kubectl scale deployment $$deploy -n "$(NAMESPACE)" --replicas=$$replicas
		echo "  ✓ $$deploy → $$replicas"
	done
	@echo "→ Waiting for deployments..."
	@for deploy in $(SCALABLE_DEPLOYS); do
		kubectl wait --namespace "$(NAMESPACE)" \
			--for=condition=Available deployment/$$deploy --timeout=180s 2>/dev/null || true
	done
	@echo "✓ All pods running"

.PHONY: cluster-restart
cluster-restart: cluster-stop cluster-start ## Stop all pods, then start them again

# ─── Database Backup / Restore ──────────────────────────────

.PHONY: db-get-pod
db-get-pod:
	@kubectl get pod -n "$(NAMESPACE)" -l app=postgres \
		-o jsonpath='{.items[0].metadata.name}' 2>/dev/null \
		|| echo ""

.PHONY: db-get-password
db-get-password:
	@kubectl get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' 2>/dev/null \
		| base64 -d 2>/dev/null || echo ""

.PHONY: db-backup
db-backup: ## Dump the Postgres database to a local file
	@POD=$$(kubectl get pod -n "$(NAMESPACE)" -l app=postgres \
		-o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
	@if [ -z "$$POD" ]; then echo "✖ No postgres pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@PASS=$$(kubectl get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' | base64 -d)
	@mkdir -p "$(BACKUP_DIR)"
	@TIMESTAMP=$$(date +"%Y%m%d_%H%M%S")
	@FILE="$(BACKUP_DIR)/nars_db_$${TIMESTAMP}.sql"
	@echo "→ Backing up database '$(DB_NAME)' from pod $$POD..."
	@kubectl exec "$$POD" -n "$(NAMESPACE)" -- env PGPASSWORD="$$PASS" \
		pg_dump -U postgres -d "$(DB_NAME)" --no-owner > "$$FILE"
	@gzip -f "$$FILE"
	@echo "✓ Backup saved: $${FILE}.gz"
	@ls -lh "$${FILE}.gz"

.PHONY: db-restore
db-restore: ## Restore a backup. Usage: make db-restore FILE=data/nars/postgres/backups/nars_db_20250101_120000.sql.gz
	@POD=$$(kubectl get pod -n "$(NAMESPACE)" -l app=postgres \
		-o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
	@if [ -z "$$POD" ]; then echo "✖ No postgres pod found in namespace '$(NAMESPACE)'"; exit 1; fi
	@if [ -z "$(FILE)" ]; then
		echo "✖ Usage: make db-restore FILE=data/nars/postgres/backups/nars_db_<timestamp>.sql.gz"
		echo ""
		echo "Available backups:"
		ls -1 "$(BACKUP_DIR)"/*.sql.gz 2>/dev/null | sed 's/^/  /' || echo "  (none)"
		exit 1
	fi
	@if [ ! -f "$(FILE)" ]; then echo "✖ File not found: $(FILE)"; exit 1; fi
	@PASS=$$(kubectl get secret nars-secrets -n "$(NAMESPACE)" \
		-o jsonpath='{.data.postgres_password}' | base64 -d)
	@echo "→ Restoring '$(FILE)' into $(DB_NAME)..."
	@echo "  ⚠ This will OVERWRITE the current database."
	@read -p "  Continue? (yes/no): " confirm; \
		if [ "$$confirm" != "yes" ]; then echo "  Cancelled."; exit 0; fi
	@if echo "$(FILE)" | grep -q '\.gz$$'; then
		gunzip -c "$(FILE)" | kubectl exec -i "$$POD" -n "$(NAMESPACE)" -- \
			env PGPASSWORD="$$PASS" psql -U postgres -d "$(DB_NAME)"
	else
		kubectl exec -i "$$POD" -n "$(NAMESPACE)" -- \
			env PGPASSWORD="$$PASS" psql -U postgres -d "$(DB_NAME)" < "$(FILE)"
	fi
	@echo "✓ Restore complete"

.PHONY: db-shell
db-shell: ## Open an interactive psql shell inside the postgres pod
	@POD=$$(kubectl get pod -n "$(NAMESPACE)" -l app=postgres \
		-o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
	@if [ -z "$$POD" ]; then echo "✖ No postgres pod found"; exit 1; fi
	@kubectl exec -it "$$POD" -n "$(NAMESPACE)" -- psql -U postgres -d "$(DB_NAME)"

# ─── Individual Steps ───────────────────────────────────────

.PHONY: cluster-create
cluster-create: ## Create the kind cluster with host-mounted postgres data (idempotent)
	@if kind get clusters 2>/dev/null | grep -q "^$(CLUSTER_NAME)$$"; then
		echo "→ Cluster '$(CLUSTER_NAME)' already exists"
	else
		echo "→ Creating postgres data directory at $(POSTGRES_DATA_DIR)..."
		mkdir -p "$(POSTGRES_DATA_DIR)"
		chmod 777 "$(POSTGRES_DATA_DIR)"
		echo "→ Generating kind config..."
		DATA_DIR="$(POSTGRES_DATA_DIR)"
		if echo "$$DATA_DIR" | grep -qv '^/'; then
			DATA_DIR="$$(cd "$$DATA_DIR" && pwd)"
		fi
		printf 'kind: Cluster\napiVersion: kind.x-k8s.io/v1alpha4\nname: %s\nnodes:\n  - role: control-plane\n    extraMounts:\n      - hostPath: %s\n        containerPath: /mnt/nars/postgres\n' \
			"$(CLUSTER_NAME)" "$$DATA_DIR" > /tmp/kind-$(CLUSTER_NAME).yaml
		echo "→ Creating kind cluster '$(CLUSTER_NAME)'..."
		kind create cluster --name "$(CLUSTER_NAME)" --config /tmp/kind-$(CLUSTER_NAME).yaml
		echo "✓ Cluster created"
	fi

.PHONY: ingress-install
ingress-install: ## Install NGINX Ingress Controller (idempotent)
	@echo "→ Installing NGINX Ingress Controller..."
	@kubectl apply -f \
		https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
	@echo "✓ Ingress controller installed"

.PHONY: ingress-wait
ingress-wait: ## Wait for ingress controller to be ready
	@echo "→ Waiting for ingress controller..."
	@kubectl wait --namespace ingress-nginx \
		--for=condition=ready pod \
		--selector=app.kubernetes.io/component=controller \
		--timeout=180s
	@echo "✓ Ingress controller ready"

.PHONY: tls-generate
tls-generate: ## Generate TLS certificate for $(DOMAIN) (idempotent)
	@if kubectl get secret nars-tls -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ TLS secret 'nars-tls' already exists"
	else
		echo "→ Generating TLS certificate for $(DOMAIN)..."
		CERT_FILE=/tmp/$(CLUSTER_NAME)-tls.crt
		KEY_FILE=/tmp/$(CLUSTER_NAME)-tls.key
		mkcert -cert-file "$$CERT_FILE" -key-file "$$KEY_FILE" "$(DOMAIN)" 2>/dev/null
		kubectl create secret tls nars-tls -n "$(NAMESPACE)" \
			--cert="$$CERT_FILE" --key="$$KEY_FILE" \
			--dry-run=client -o yaml \
		| kubectl apply -f -
		echo "✓ TLS secret created"
	fi

.PHONY: ca-secret
ca-secret: ## Create mTLS CA secret from k8s/certs/ca.crt (idempotent)
	@if kubectl get secret nars-ca -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ CA secret 'nars-ca' already exists"
	else
		echo "→ Creating mTLS CA secret..."
		kubectl create secret generic nars-ca -n "$(NAMESPACE)" \
			--from-file=ca.crt="$(K8S_DIR)/certs/ca.crt" \
			--dry-run=client -o yaml \
		| kubectl apply -f -
		echo "✓ CA secret created"
	fi

.PHONY: secrets-apply
secrets-apply: ## Create nars-secrets and regcred with generated/variable values
	@echo "→ Creating 'nars-secrets'..."
	@kubectl create secret generic nars-secrets -n "$(NAMESPACE)" \
		--from-literal=postgres_password="$(POSTGRES_PASSWORD)" \
		--from-literal=ConnectionStrings__DefaultConnection=\
"Host=postgres;Port=5432;Database=nars_db;Username=postgres;Password=$(POSTGRES_PASSWORD)" \
		--from-literal=Jwt__SecretKey="$(JWT_SECRET)" \
		--dry-run=client -o yaml \
	| kubectl apply -f -
	@echo "✓ nars-secrets created"

	@if [ -n "$(DOCKER_TOKEN)" ]; then
		echo "→ Creating 'regcred'..."
		kubectl create secret docker-registry regcred -n "$(NAMESPACE)" \
			--docker-server=https://index.docker.io/v1/ \
			--docker-username="$(DOCKER_USERNAME)" \
			--docker-password="$(DOCKER_TOKEN)" \
			--dry-run=client -o yaml \
		| kubectl apply -f -
		echo "✓ regcred created"
	else
		echo "→ Skipping regcred (DOCKER_TOKEN not set — using locally loaded images)"
	fi

.PHONY: kustomize-apply
kustomize-apply: ## Apply k8s manifests via kustomize
	@echo "→ Applying kustomization..."
	@kubectl apply -k "$(K8S_DIR)"
	@echo "✓ Kustomization applied"

	@echo "→ Waiting for deployments..."
	@for deploy in postgres nars-api nars-frontend; do
		if kubectl get deployment $$deploy -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
			kubectl wait --namespace "$(NAMESPACE)" \
				--for=condition=Available deployment/$$deploy --timeout=180s
		fi
	done
	@echo "✓ All deployments ready"

# ─── Docker Images ───────────────────────────────────────────

.PHONY: images-build
images-build: ## Build all Docker images
	@echo "→ Building images..."
	$(MAKE) _build-nars-api
	$(MAKE) _build-nars-postgres
	$(MAKE) _build-nars-vite
	@echo "✓ All images built"

.PHONY: _build-nars-api
_build-nars-api:
	@echo "  → $(DOCKER_ORG)/nars-api:latest"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-api" \
		-t "$(DOCKER_ORG)/nars-api:latest" .

.PHONY: _build-nars-postgres
_build-nars-postgres:
	@echo "  → $(DOCKER_ORG)/nars-postgres:latest"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.postgres" \
		-t "$(DOCKER_ORG)/nars-postgres:latest" .

.PHONY: _build-nars-vite
_build-nars-vite:
	@echo "  → $(DOCKER_ORG)/nars-vite:latest"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-vite" \
		-t "$(DOCKER_ORG)/nars-vite:latest" .

.PHONY: images-push
images-push: ## Push all Docker images to registry
	@for img in $(REGISTRY_IMAGES); do
		echo "→ Pushing $(DOCKER_ORG)/$$img:latest..."
		docker push "$(DOCKER_ORG)/$$img:latest"
	done
	@echo "✓ All images pushed"

.PHONY: images-load
images-load: ## Load locally built Docker images into the kind cluster
	@for img in $(REGISTRY_IMAGES); do
		full="$(DOCKER_ORG)/$$img:latest"
		if docker image inspect "$$full" >/dev/null 2>&1; then
			echo "→ Loading $$full into cluster..."
			kind load docker-image "$$full" --name "$(CLUSTER_NAME)"
		else
			echo "  ⚠ $$full not found locally — will pull from registry"
		fi
	done
	@echo "✓ Images loaded"
