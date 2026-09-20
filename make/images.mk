# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: Docker image build/push/load.


# ─── Docker Images ───────────────────────────────────────────

# Override IMAGE_TAG to pin a specific version (e.g., IMAGE_TAG=abc1234).
# Defaults to 'latest' for local dev. CI/CD should set this to the commit SHA.
IMAGE_TAG ?= latest

# Shell-safe single-quoted form of IMAGE_TAG. Tags are developer/CI-supplied
# and get interpolated into shell contexts (awk -v, echo, grep, docker -t),
# where backticks or a stray `"` would be executed. Single-quote escaping keeps
# every character literal. Recipes compose it by breaking out of surrounding
# double quotes: "…org/name:"$(IMAGE_TAG_Q). Charset-guard prerequisites
# (_warn-latest-tag / _check-pinned-tag) additionally reject hostile tags.
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

# Requirement: images-build builds ONLY what changed. Each _build-nars-* stores a
# content-stamp (sha256 of its Dockerfile + every COPY source file). The stamp dir
# lives beside the kind staging TMPDIR (root FS, NOT the /tmp tmpfs) so it survives
# reboots and is never reclaimed. A rebuild runs only when a stamped image's stamp
# differs from its current content hash — identical sources → no docker build.
# The per-image glob set MUST stay in lockstep with .github/workflows/docker.yml's
# paths-filter (it encodes the canonical "what counts as a source change"), and
# git-ignored paths are excluded from the hash for the same reason paths-filter
# diffs tracked files only — on-disk build artifacts (bin/, obj/, node_modules/)
# must never re-trigger a local rebuild.
IMAGES_HASH_DIR ?= $(CURDIR)/.image-hashes
.PHONY: _ensure-images-hash-dir _build-nars-api _build-nars-postgis _build-nars-vite _build-nars-backup _build-nars-segma
_ensure-images-hash-dir:
	@mkdir -p "$(IMAGES_HASH_DIR)"


.PHONY: images-build
images-build: _warn-latest-tag ## Build all Docker images
	@echo "→ Building images..."
	$(SUBMAKE) _build-nars-api
	$(SUBMAKE) _build-nars-postgis
	$(SUBMAKE) _build-nars-vite
	$(SUBMAKE) _build-nars-backup
	$(SUBMAKE) _build-nars-segma
	@echo "✓ All images built"

.PHONY: _build-nars-api
_build-nars-api: _warn-latest-tag
	@img=nars-api; st="$(IMAGES_HASH_DIR)/$$img"; \
	if __image_guard "$$st.guard" 'nars-api/**' 'nars-infra/docker/Dockerfile.nars-api' 'Directory.Build.props'; then \
		echo "  → skipping $$img: unchanged content-hash (CI paths-filter agrees)"; \
	else \
		echo "  → $(DOCKER_ORG)/nars-api:"$(IMAGE_TAG_Q); \
		docker build -f "$(DOCKER_DIR)/Dockerfile.nars-api" \
			-t "$(DOCKER_ORG)/nars-api:"$(IMAGE_TAG_Q) .; \
	fi

.PHONY: _build-nars-postgis
_build-nars-postgis: _warn-latest-tag
	@img=nars-postgis; st="$(IMAGES_HASH_DIR)/$$img"; \
	if __image_guard "$$st.guard" 'nars-infra/scripts/**' 'nars-infra/docker/Dockerfile.nars-postgis' 'docs/seed_reference_data.sql'; then \
		echo "  → skipping $$img: unchanged content-hash (CI paths-filter agrees)"; \
	else \
		echo "  → $(DOCKER_ORG)/nars-postgis:"$(IMAGE_TAG_Q); \
		docker build -f "$(DOCKER_DIR)/Dockerfile.nars-postgis" \
			-t "$(DOCKER_ORG)/nars-postgis:"$(IMAGE_TAG_Q) .; \
	fi

.PHONY: _build-nars-vite
_build-nars-vite: _warn-latest-tag
	@img=nars-vite; st="$(IMAGES_HASH_DIR)/$$img"; \
	if __image_guard "$$st.guard" 'nars-web/**' 'nars-infra/docker/Dockerfile.nars-vite' 'nars-infra/docker/nginx.nars-vite.conf' 'nars-infra/docker/proxy-common-snippet.conf'; then \
		echo "  → skipping $$img: unchanged content-hash (CI paths-filter agrees)"; \
	else \
		echo "  → $(DOCKER_ORG)/nars-vite:"$(IMAGE_TAG_Q); \
		docker build -f "$(DOCKER_DIR)/Dockerfile.nars-vite" \
			-t "$(DOCKER_ORG)/nars-vite:"$(IMAGE_TAG_Q) .; \
	fi

.PHONY: _build-nars-backup
_build-nars-backup: _warn-latest-tag
	@img=nars-backup; st="$(IMAGES_HASH_DIR)/$$img"; \
	if __image_guard "$$st.guard" 'nars-infra/scripts/**' 'nars-infra/docker/Dockerfile.nars-backup' 'docs/seed_reference_data.sql'; then \
		echo "  → skipping $$img: unchanged content-hash (CI paths-filter agrees)"; \
	else \
		echo "  → $(DOCKER_ORG)/nars-backup:"$(IMAGE_TAG_Q); \
		docker build -f "$(DOCKER_DIR)/Dockerfile.nars-backup" \
			-t "$(DOCKER_ORG)/nars-backup:"$(IMAGE_TAG_Q) .; \
	fi

.PHONY: _build-nars-segma
_build-nars-segma: _warn-latest-tag
	@img=nars-segma; st="$(IMAGES_HASH_DIR)/$$img"; \
	if __image_guard "$$st.guard" 'nars-segma/**' 'nars-infra/docker/Dockerfile.nars-segma'; then \
		echo "  → skipping $$img: unchanged content-hash (CI paths-filter agrees)"; \
	else \
		echo "  → $(DOCKER_ORG)/nars-segma:"$(IMAGE_TAG_Q); \
		docker build -f "$(DOCKER_DIR)/Dockerfile.nars-segma" \
			-t "$(DOCKER_ORG)/nars-segma:"$(IMAGE_TAG_Q) nars-segma/; \
	fi

.PHONY: images-push
images-push: _check-pinned-tag _warn-latest-tag ## Push all Docker images to registry
	@	for img in $(REGISTRY_IMAGES); do
		echo "→ Pushing $(DOCKER_ORG)/$$img:"$(IMAGE_TAG_Q)"..."
		docker push "$(DOCKER_ORG)/$$img:"$(IMAGE_TAG_Q)
	done
	@echo "✓ All images pushed"

# Where `kind load docker-image` stages the intermediate docker-save tarball
# (kind streams the archive into cluster nodes via a staging dir under TMPDIR,
# defaulting to the host's /tmp -- a ~7.8G tmpfs on this box whose multi-GB
# nars-segma save overflows it -> "disk quota exceeded" Killing cluster-up).
# Redirect staging to the root filesystem (~33G free) and create it fresh per
# invocation. Durable: every images-load/frontend-update run re-creates it and
# exports TMPDIR only for that kind subprocess, never the caller's shell.
KIND_STAGING_TMPDIR ?= $(CURDIR)/.kind-staging-tmp
KIND_TMPDIR_MKDIR = mkdir -p "$(KIND_STAGING_TMPDIR)"
KIND_TMPDIR_EXPORT = TMPDIR="$(KIND_STAGING_TMPDIR)"

# Fail-fast disk guard for kind image staging. Previously a low-disk host
# would only surface the problem AFTER docker-save had been streaming for
# minutes (silent until ENOSPC kills kind mid-load). Now: before any staging
# happens, assert the staging filesystem has >= KIND_STAGING_SPACE_MIN KiB
# free, and print a readable remediation if not.
KIND_STAGING_SPACE_MIN_KB ?= 15728640   # 15GiB headroom floor
.PHONY: _guard-kind-staging-space
_guard-kind-staging-space: _warn-latest-tag
	@staging="$(KIND_STAGING_TMPDIR)"; mkdir -p "$$staging"; \
	free_kb=$$(df -P -k "$$staging" | awk 'NR==2 {print $$4}'); \
	need=$(KIND_STAGING_SPACE_MIN_KB); \
	if [ "$$free_kb" -lt "$$need" ]; then \
		echo "✗ Kind image staging needs >= $$need KiB free on $$staging (host FS, NOT the /tmp tmpfs)."; \
		echo "  Free: $$free_kb KiB. Free space or run:  docker builder prune -f"; \
		echo "  (guarded before anything is staged, so this fails fast)"; \
		exit 2; \
	fi

.PHONY: images-load
images-load: _warn-latest-tag _guard-kind-staging-space ## Load locally built Docker images into the kind cluster
	@for img in $(REGISTRY_IMAGES); do
		full="$(DOCKER_ORG)/$$img:"$(IMAGE_TAG_Q)
		if docker image inspect "$$full" >/dev/null 2>&1; then
			echo "→ Loading $$full into cluster..."
			$(KIND_TMPDIR_MKDIR) && $(KIND_TMPDIR_EXPORT) $(KIND) load docker-image "$$full" --name "$(CLUSTER_NAME)"
		else
			echo "  ⚠ $$full not found locally — pods will fail to start unless regcred is configured"
		fi
	done
	@echo "✓ Images loaded"

# The SPA has two HTML entrypoints that must reference the SAME hashed bundle:
# / (nginx image) and /map (nars-api's own wwwroot copy). Redeploying only
# nars-vite makes /map stale and 404s its bundle -> blank page after login.
# frontend-update therefore redeploys BOTH images atomically and then verifies
# the running pods agree.
.PHONY: frontend-update
frontend-update: _warn-latest-tag _guard-kind-staging-space ## Rebuild nars-vite + sync nars-api/wwwroot, load, rollout restart, verify bundle sync
	@echo "→ Rebuilding nars-web and syncing nars-api/wwwroot..."
	@(cd nars-web && npm run build:deploy)
	@$(SUBMAKE) _build-nars-vite
	@$(SUBMAKE) _build-nars-api
	@$(KIND_TMPDIR_MKDIR) && $(KIND_TMPDIR_EXPORT) $(KIND) load docker-image "$(DOCKER_ORG)/nars-vite:"$(IMAGE_TAG_Q) --name "$(CLUSTER_NAME)"
	@$(KIND_TMPDIR_MKDIR) && $(KIND_TMPDIR_EXPORT) $(KIND) load docker-image "$(DOCKER_ORG)/nars-api:"$(IMAGE_TAG_Q) --name "$(CLUSTER_NAME)"
	@$(KUBECTL) rollout restart deployment nars-frontend -n "$(NAMESPACE)"
	@$(KUBECTL) rollout status deployment nars-frontend -n "$(NAMESPACE)" --timeout=120s
	@$(KUBECTL) rollout restart deployment nars-api -n "$(NAMESPACE)"
	@$(KUBECTL) rollout status deployment nars-api -n "$(NAMESPACE)" --timeout=180s
	@$(SUBMAKE) _check-bundle-sync
	@echo "✓ nars-vite + nars-api wwwroot rebuilt, deployed, and bundle-sync verified"

# Live post-deploy guard: fetch the index.html served by BOTH entrypoints from
# the running pods and assert they reference the identical bundle assets, and
# that the entry bundle exists in both images. Catches any future drift between
# the nginx-served / and the API-served /map (the blank-page-after-login bug).
.PHONY: _check-bundle-sync
_check-bundle-sync:
	@echo "→ Verifying / and /map serve the same bundle..."
	@tmp=$$(mktemp -d); trap 'rm -rf "$$tmp"' EXIT; \
	if $(KUBECTL) exec -n "$(NAMESPACE)" deploy/nars-frontend -- cat /usr/share/nginx/html/index.html > "$$tmp/frontend.html" 2>/dev/null \
		&& $(KUBECTL) exec -n "$(NAMESPACE)" deploy/nars-api -- cat /app/wwwroot/index.html > "$$tmp/api.html" 2>/dev/null \
		&& python3 nars-infra/scripts/check_frontend_bundle_sync.py --frontend "$$tmp/frontend.html" --api "$$tmp/api.html"; then \
		refs=$$(grep -oE 'assets/[A-Za-z0-9._-]+' "$$tmp/api.html" | sed 's#assets/##' | sort -u); \
		for f in $$refs; do \
			$(KUBECTL) exec -n "$(NAMESPACE)" deploy/nars-frontend -- sh -c 'test -f /usr/share/nginx/html/assets/'"$$f" || { echo "  ✖ $$f missing in nars-vite image (entrypoint mismatch!)"; exit 1; }; \
			$(KUBECTL) exec -n "$(NAMESPACE)" deploy/nars-api -- sh -c 'test -f /app/wwwroot/assets/'"$$f" || { echo "  ✖ $$f missing in nars-api image (entrypoint mismatch!)"; exit 1; }; \
		done; \
		count=$$(echo $$refs | wc -w); \
		[ "$$count" -gt 0 ] && echo "  ✓ all $$count referenced assets present in both images"; \
	else \
		echo "  ✖ bundle sync check failed — / and /map disagree (run make frontend-update)"; \
		exit 1; \
	fi
export IMAGES_SCRIPTS_DIR := $(CURDIR)/make/scripts
export PATH := $(IMAGES_SCRIPTS_DIR):$(PATH)
