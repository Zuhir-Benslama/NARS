# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: cross-project code quality gates.


# ─── Code Quality (nars-infra) ──────────────────────────────

.PHONY: lint
lint: ## Run cross-project linting (.NET format + infra linters)
	dotnet format Workspace.sln --verify-no-changes --no-restore
	$(SUBMAKE) infra-lint

.PHONY: infra-lint
infra-lint: ## Run all nars-infra linters (shell, docker, yaml, python, node, makefile, checkmake, tag guard)
	$(SUBMAKE) infra-lint-shell
	$(SUBMAKE) infra-lint-docker
	$(SUBMAKE) infra-lint-yaml
	$(SUBMAKE) infra-lint-python
	$(SUBMAKE) infra-lint-node
	$(SUBMAKE) infra-lint-makefile
	$(SUBMAKE) infra-lint-checkmake
	$(SUBMAKE) infra-lint-tag-guard
	$(SUBMAKE) infra-lint-local-ingress-guard

# File lists resolved by make ($(wildcard)) at parse time and /mnt-prefixed
# for container use. Shell globs like /mnt/**/*.yaml must NOT be used in
# docker fallbacks: they expand against the HOST filesystem (where /mnt is
# unrelated) and reach the container as a literal unexpanded pattern, which
# none of these tools glob internally. This bug silently broke the
# shellcheck/hadolint/yamllint docker fallbacks before.
SHELL_SCRIPTS     := $(wildcard nars-infra/scripts/*.sh)
DOCKERFILES       := $(wildcard nars-infra/docker/Dockerfile.*)
YAML_FILES        := $(wildcard nars-infra/k8s/*.yaml nars-infra/k8s/helm-values/*.yaml nars-infra/roads/*.yaml)
NODE_SCRIPTS      := $(wildcard nars-infra/scripts/*.mjs)
SHELL_SCRIPTS_MNT := $(patsubst %,/mnt/%,$(SHELL_SCRIPTS))
DOCKERFILES_MNT   := $(patsubst %,/mnt/%,$(DOCKERFILES))
YAML_FILES_MNT    := $(patsubst %,/mnt/%,$(YAML_FILES))

.PHONY: infra-lint-shell
infra-lint-shell: ## Shell-check nars-infra/scripts/*.sh
	@if command -v shellcheck >/dev/null 2>&1; then
		shellcheck $(SHELL_SCRIPTS)
	else
		docker run --rm -v "$$(pwd):/mnt" $(SHELLCHECK_IMAGE) $(SHELL_SCRIPTS_MNT)
	fi

.PHONY: infra-lint-docker
infra-lint-docker: ## Lint Dockerfiles with hadolint
	@if command -v hadolint >/dev/null 2>&1; then
		hadolint --failure-threshold error $(DOCKERFILES)
	else
		docker run --rm \
			-v "$$(pwd):/mnt" \
			-v "$$(pwd)/nars-infra/.hadolint.yaml:/cfg/hadolint.yaml:ro" \
			$(HADOLINT_IMAGE) hadolint --config /cfg/hadolint.yaml \
			--failure-threshold error $(DOCKERFILES_MNT)
	fi

.PHONY: infra-lint-yaml
infra-lint-yaml: ## Lint k8s YAML with yamllint (uses .yamllint.yaml config)
	@if command -v yamllint >/dev/null 2>&1; then
		yamllint -c nars-infra/.yamllint.yaml $(YAML_FILES)
	else
		docker run --rm -v "$$(pwd):/mnt" $(YAMLLINT_IMAGE) \
			-c /mnt/nars-infra/.yamllint.yaml $(YAML_FILES_MNT)
	fi

.PHONY: infra-lint-python
infra-lint-python: ## Lint Python scripts with ruff (check + format)
	@if command -v ruff >/dev/null 2>&1; then
		ruff check nars-infra/scripts/ nars-roads/app/ nars-roads/tests/
		ruff format --check nars-infra/scripts/ nars-roads/app/ nars-roads/tests/
	else
		docker run --rm -v "$$(pwd):/mnt" $(RUFF_IMAGE) check /mnt/nars-infra/scripts/ /mnt/nars-roads/app/ /mnt/nars-roads/tests/
		docker run --rm -v "$$(pwd):/mnt" $(RUFF_IMAGE) format --check /mnt/nars-infra/scripts/ /mnt/nars-roads/app/ /mnt/nars-roads/tests/
	fi

.PHONY: infra-lint-node
infra-lint-node: ## Syntax-check Node helper scripts
	@if [ -z "$(NODE_SCRIPTS)" ]; then echo "✓ No .mjs scripts to check"; exit 0; fi
	if command -v node >/dev/null 2>&1; then
		for f in $(NODE_SCRIPTS); do node --check "$$f"; done
	else
		docker run --rm -v "$$(pwd):/mnt" $(NODE_IMAGE) \
			sh -c 'for f in $(NODE_SCRIPTS); do node --check "/mnt/$$f"; done'
	fi

.PHONY: infra-lint-makefile
infra-lint-makefile: ## Validate Makefile syntax with dry-run
	@echo "→ Checking Makefile syntax..."
	@make -n help > /dev/null 2>&1 && echo "✓ Makefile syntax OK" \
		|| { echo "✖ Makefile syntax error"; exit 1; }
	@echo "→ Checking undefined variable references..."
	# GNUMAKEFLAGS is a GNU Make internal variable spuriously flagged by
	# --warn-undefined-variables -Rr (make 4.4+); filter it out.
	# Fails when references are found — `|| true` only handles the clean case.
	@make -Rr --warn-undefined-variables -n help 2>&1 | grep -i 'warning.*undefined' | grep -v GNUMAKEFLAGS \
		&& { echo "✖ Undefined variable references found (see above)"; exit 1; } \
		|| echo "✓ No undefined variable references"

.PHONY: infra-lint-checkmake
infra-lint-checkmake: ## Lint the root Makefile with checkmake (config: checkmake.ini)
	@if command -v checkmake >/dev/null 2>&1; then
		checkmake --config=checkmake.ini Makefile
	else
		docker run --rm -v "$$(pwd):/mnt":ro --entrypoint /checkmake \
			$(CHECKMAKE_IMAGE) --config=/mnt/checkmake.ini /mnt/Makefile
	fi


# Internal: warn when the mutable 'latest' tag is in use (build/push/load).
.PHONY: _warn-latest-tag
_warn-latest-tag:
	@if $(_check_tag_cmd); then \
		echo '✖ IMAGE_TAG='$(IMAGE_TAG_Q)' contains invalid characters (only alphanumeric, dots, hyphens, underscores allowed)'; \
		exit 1; \
	fi
	@if echo $(IMAGE_TAG_Q) | grep -qi "^latest$$"; then \
		echo "  ⚠ IMAGE_TAG=latest — set IMAGE_TAG=<commit-sha> for CI/CD builds"; \
	fi

.PHONY: _check-pinned-tag
_check-pinned-tag: ## Fail if deploying with the mutable 'latest' tag outside local dev
	@if $(_check_tag_cmd); then \
		echo '✖ IMAGE_TAG='$(IMAGE_TAG_Q)' contains invalid characters (only alphanumeric, dots, hyphens, underscores allowed)'; \
		exit 1; \
	fi
	@if [ "$(ALLOW_LATEST)" != "1" ] && [ "$(DEPLOY_ENV)" != "dev" ] && echo $(IMAGE_TAG_Q) | grep -qi "^latest$$"; then
		echo "✖ Refusing to deploy IMAGE_TAG=latest in $(DEPLOY_ENV) — mutable tags break reproducible deployments.";
		echo "  Set IMAGE_TAG=<commit-sha>, or DEPLOY_ENV=dev, or ALLOW_LATEST=1 to override.";
		exit 1;
	fi

.PHONY: _check-local-ingresses
_check-local-ingresses: ## Fail if dev-only local ingresses would be deployed outside local dev
	@if [ "$(DEPLOY_ENV)" != "dev" ]; then
		output=$$($(KUBECTL) kustomize "$(K8S_DIR)" 2>&1) || { echo "✖ kubectl kustomize failed — cannot verify local ingresses are absent"; exit 1; };
		if echo "$$output" | grep -qE "name: nars-(api|frontend)-local"; then
			echo "✖ Refusing to deploy dev-only local ingresses (nars-api-local / nars-frontend-local) in $(DEPLOY_ENV).";
			echo "  They expose /api and /login WITHOUT mTLS and match any Host.";
			echo "  Exclude them via a production overlay, or set DEPLOY_ENV=dev.";
			exit 1;
		fi
	fi

.PHONY: infra-lint-tag-guard
infra-lint-tag-guard: ## Assert _check-pinned-tag rejects 'latest' outside dev (self-test)
	@echo "→ Verifying _check-pinned-tag rejects IMAGE_TAG=latest in production..."
	@if DEPLOY_ENV=production IMAGE_TAG=latest ALLOW_LATEST= $(SUBMAKE) _check-pinned-tag >/dev/null 2>&1; then
		echo "✖ _check-pinned-tag unexpectedly accepted latest in production";
		exit 1;
	fi
	@echo "  ✓ latest rejected in production"
	@echo "→ Verifying _check-pinned-tag accepts a pinned tag in production..."
	@DEPLOY_ENV=production IMAGE_TAG=abc123 ALLOW_LATEST= $(SUBMAKE) _check-pinned-tag
	@echo "  ✓ pinned tag accepted in production"
	@echo "→ Verifying _check-pinned-tag rejects a hostile tag (shell metacharacters)..."
	@if DEPLOY_ENV=production IMAGE_TAG='a\`rm\`b' ALLOW_LATEST= $(SUBMAKE) _check-pinned-tag >/dev/null 2>&1; then
		echo "✖ _check-pinned-tag unexpectedly accepted a tag containing shell metacharacters";
		exit 1;
	fi
	@echo "  ✓ hostile tag rejected"
	@echo "→ Verifying ALLOW_LATEST=1 overrides the guard..."
	@DEPLOY_ENV=production IMAGE_TAG=latest ALLOW_LATEST=1 $(SUBMAKE) _check-pinned-tag
	@echo "  ✓ ALLOW_LATEST=1 override accepted"

.PHONY: infra-lint-local-ingress-guard
infra-lint-local-ingress-guard: ## Assert _check-local-ingresses rejects local ingresses outside dev (self-test)
	@echo "→ Verifying _check-local-ingresses rejects the base kustomization in production (it ships nars-api-local)..."
	@if DEPLOY_ENV=production $(SUBMAKE) _check-local-ingresses >/dev/null 2>&1; then
		echo "✖ _check-local-ingresses unexpectedly accepted dev local ingresses in production";
		exit 1;
	fi
	@echo "  ✓ local ingresses rejected in production"
	@echo "→ Verifying _check-local-ingresses passes in dev..."
	@DEPLOY_ENV=dev $(SUBMAKE) _check-local-ingresses
	@echo "  ✓ local ingresses allowed in dev"
