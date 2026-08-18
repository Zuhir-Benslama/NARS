# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: Docker image build/push/load.


# ─── Docker Images ───────────────────────────────────────────

# Override IMAGE_TAG to pin a specific version (e.g., IMAGE_TAG=abc1234).
# Defaults to 'latest' for local dev. CI/CD should set this to the commit SHA.
IMAGE_TAG ?= latest

# Shell-safe single-quoted form of IMAGE_TAG. Tags are developer/CI-supplied
# and get interpolated into double-quoted shell contexts (awk -v, echo, grep),
# where backticks or a stray `"` would be executed. Single-quote escaping keeps
# every character literal. Use this instead of the raw variable in recipes.
IMAGE_TAG_Q = '$(subst ','"'"',$(IMAGE_TAG))'

# Charset guard for IMAGE_TAG, shared by _warn-latest-tag and _check-pinned-tag.
# Evaluated against the escaped value, so a hostile tag is rejected instead of
# being interpolated into a shell command. Same whitelist as
# kustomize-set-image-tag's validation.
_check_tag_cmd = echo $(IMAGE_TAG_Q) | grep -qE '[^a-zA-Z0-9._-]'

# DEPLOY_ENV gates use of the mutable 'latest' tag:
#   dev (default)       — local kind loop; 'latest' allowed
#   production/staging  — 'latest' refused; deployments must pin IMAGE_TAG=<sha>
# Set ALLOW_LATEST=1 for a deliberate emergency manual rollout.
DEPLOY_ENV ?= dev
ALLOW_LATEST ?=


.PHONY: images-build
images-build: _warn-latest-tag ## Build all Docker images
	@echo "→ Building images..."
	$(SUBMAKE) _build-nars-api
	$(SUBMAKE) _build-nars-postgis
	$(SUBMAKE) _build-nars-vite
	$(SUBMAKE) _build-nars-backup
	$(SUBMAKE) _build-nars-roads
	@echo "✓ All images built"

.PHONY: _build-nars-api
_build-nars-api: _warn-latest-tag
	@echo "  → $(DOCKER_ORG)/nars-api:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-api" \
		-t "$(DOCKER_ORG)/nars-api:$(IMAGE_TAG)" .

.PHONY: _build-nars-postgis
_build-nars-postgis: _warn-latest-tag
	@echo "  → $(DOCKER_ORG)/nars-postgis:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-postgis" \
		-t "$(DOCKER_ORG)/nars-postgis:$(IMAGE_TAG)" .

.PHONY: _build-nars-vite
_build-nars-vite: _warn-latest-tag
	@echo "  → $(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-vite" \
		-t "$(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)" .

.PHONY: _build-nars-backup
_build-nars-backup: _warn-latest-tag
	@echo "  → $(DOCKER_ORG)/nars-backup:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-backup" \
		-t "$(DOCKER_ORG)/nars-backup:$(IMAGE_TAG)" .

.PHONY: _build-nars-roads
_build-nars-roads: _warn-latest-tag
	@echo "  → $(DOCKER_ORG)/nars-roads:$(IMAGE_TAG)"
	@docker build -f "$(DOCKER_DIR)/Dockerfile.nars-roads" \
		-t "$(DOCKER_ORG)/nars-roads:$(IMAGE_TAG)" nars-roads/

.PHONY: images-push
images-push: _check-pinned-tag _warn-latest-tag ## Push all Docker images to registry
	@	for img in $(REGISTRY_IMAGES); do
		echo "→ Pushing $(DOCKER_ORG)/$$img:$(IMAGE_TAG)..."
		docker push "$(DOCKER_ORG)/$$img:$(IMAGE_TAG)"
	done
	@echo "✓ All images pushed"

.PHONY: images-load
images-load: _warn-latest-tag ## Load locally built Docker images into the kind cluster
	@for img in $(REGISTRY_IMAGES); do
		full="$(DOCKER_ORG)/$$img:$(IMAGE_TAG)"
		if docker image inspect "$$full" >/dev/null 2>&1; then
			echo "→ Loading $$full into cluster..."
			kind load docker-image "$$full" --name "$(CLUSTER_NAME)"
		else
			echo "  ⚠ $$full not found locally — pods will fail to start unless regcred is configured"
		fi
	done
	@echo "✓ Images loaded"

.PHONY: frontend-update
frontend-update: _warn-latest-tag ## Rebuild nars-vite, load into kind, and rollout restart
	$(SUBMAKE) _build-nars-vite
	@kind load docker-image "$(DOCKER_ORG)/nars-vite:$(IMAGE_TAG)" --name "$(CLUSTER_NAME)"
	@$(KUBECTL) rollout restart deployment nars-frontend -n "$(NAMESPACE)"
	@$(KUBECTL) rollout status deployment nars-frontend -n "$(NAMESPACE)" --timeout=120s
	@echo "✓ nars-vite rebuilt and deployed"
