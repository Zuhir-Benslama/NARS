# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: cross-project code quality gates.


# ─── Code Quality (nars-infra) ──────────────────────────────

.PHONY: lint
lint: ## Run cross-project linting (.NET format + infra linters)
	@dotnet format Workspace.sln --verify-no-changes --no-restore
	$(SUBMAKE) infra-lint

.PHONY: infra-lint
infra-lint: ## Run all nars-infra linters (shell, docker, yaml, python, node, makefile, checkmake, sql, nginx, tag guard, kind-cidr guard, local-ingress guard, observability security, markdown, uml drift, migration drift, image guard)
	@$(SUBMAKE) infra-lint-shell
	$(SUBMAKE) infra-lint-docker
	$(SUBMAKE) infra-lint-yaml
	$(SUBMAKE) infra-lint-python
	$(SUBMAKE) infra-lint-node
	$(SUBMAKE) infra-lint-makefile
	$(SUBMAKE) infra-lint-checkmake
	$(SUBMAKE) infra-lint-sql
	$(SUBMAKE) infra-lint-nginx
	$(SUBMAKE) infra-lint-tag-guard
	$(SUBMAKE) infra-lint-digest-guard
	$(SUBMAKE) infra-lint-kind-cidr-guard
	$(SUBMAKE) infra-lint-local-ingress-guard
	$(SUBMAKE) infra-lint-observability-security
	$(SUBMAKE) infra-lint-markdown
	$(SUBMAKE) infra-lint-uml-drift
	$(SUBMAKE) infra-lint-migration-drift
	$(SUBMAKE) infra-lint-frontend-wwwroot-sync
	$(SUBMAKE) infra-lint-image-guard

# File lists resolved by make ($(wildcard)) at parse time and /mnt-prefixed
# for container use. Shell globs like /mnt/**/*.yaml must NOT be used in
# docker fallbacks: they expand against the HOST filesystem (where /mnt is
# unrelated) and reach the container as a literal unexpanded pattern, which
# none of these tools glob internally. This bug silently broke the
# shellcheck/hadolint/yamllint docker fallbacks before.
SHELL_SCRIPTS     := $(wildcard nars-infra/scripts/*.sh)
DOCKERFILES       := $(wildcard nars-infra/docker/Dockerfile.*)
YAML_FILES        := $(wildcard nars-infra/k8s/*.yaml nars-infra/k8s/helm-values/*.yaml nars-infra/segma/*.yaml nars-infra/overlays/*.yaml nars-infra/overlays/*/*.yaml nars-infra/overlays/*/*/*.yaml .github/workflows/*.yml)
NODE_SCRIPTS      := $(wildcard nars-infra/scripts/*.mjs)
MIGRATIONS_SQL    := $(wildcard nars-infra/migrations/*.sql nars-infra/scripts/postgis-migration-baseline.sql)
# docs/**/*.md — markdown lint gate (infra-lint-markdown). UML diagrams are
# additionally gated by docs-lint-uml (render) and infra-lint-uml-drift
# (nars-class-diagram.md vs nars-api source).
DOCS_MD           := $(wildcard docs/*.md docs/uml/*.md)
# nginx.nars-vite.conf is not a wildcard target — it is the frontend server block
NGINX_CONF        := nars-infra/docker/nginx.nars-vite.conf
NGINX_SNIPPET     := nars-infra/docker/proxy-common-snippet.conf
SHELL_SCRIPTS_MNT := $(patsubst %,/mnt/%,$(SHELL_SCRIPTS))
DOCKERFILES_MNT   := $(patsubst %,/mnt/%,$(DOCKERFILES))
YAML_FILES_MNT    := $(patsubst %,/mnt/%,$(YAML_FILES))
MIGRATIONS_SQL_MNT := $(patsubst %,/mnt/%,$(MIGRATIONS_SQL))
DOCS_MD_MNT       := $(patsubst %,/mnt/%,$(DOCS_MD))

.PHONY: infra-lint-shell
infra-lint-shell: ## Shell-check nars-infra/scripts/*.sh
	@if [ -z "$(SHELL_SCRIPTS)" ]; then echo "✓ No shell scripts to check"; exit 0; fi
	@if command -v shellcheck >/dev/null 2>&1; then
		shellcheck $(SHELL_SCRIPTS)
	else
		docker run --rm -v "$$(pwd):/mnt" $(SHELLCHECK_IMAGE) $(SHELL_SCRIPTS_MNT)
	fi

.PHONY: infra-lint-docker
infra-lint-docker: ## Lint Dockerfiles with hadolint
	@if [ -z "$(DOCKERFILES)" ]; then echo "✓ No Dockerfiles to check"; exit 0; fi
	@if command -v hadolint >/dev/null 2>&1; then
		hadolint --config nars-infra/.hadolint.yaml --failure-threshold error $(DOCKERFILES)
	else
		docker run --rm \
			-v "$$(pwd):/mnt" \
			-v "$$(pwd)/nars-infra/.hadolint.yaml:/cfg/hadolint.yaml:ro" \
			$(HADOLINT_IMAGE) hadolint --config /cfg/hadolint.yaml \
			--failure-threshold error $(DOCKERFILES_MNT)
	fi

.PHONY: infra-lint-yaml
infra-lint-yaml: ## Lint k8s + GitHub Actions YAML with yamllint (uses .yamllint.yaml config)
	@if [ -z "$(YAML_FILES)" ]; then echo "✓ No YAML files to check"; exit 0; fi
	@if command -v yamllint >/dev/null 2>&1; then
		yamllint -c nars-infra/.yamllint.yaml $(YAML_FILES)
	else
		docker run --rm -v "$$(pwd):/mnt" $(YAMLLINT_IMAGE) \
			-c /mnt/nars-infra/.yamllint.yaml $(YAML_FILES_MNT)
	fi

.PHONY: infra-lint-python
infra-lint-python: ## Lint Python scripts with ruff (check + format)
	@if command -v ruff >/dev/null 2>&1; then
		ruff check nars-infra/scripts/ nars-segma/app/ nars-segma/tests/
		ruff format --check nars-infra/scripts/ nars-segma/app/ nars-segma/tests/
	else
		docker run --rm -v "$$(pwd):/mnt" $(RUFF_IMAGE) check /mnt/nars-infra/scripts/ /mnt/nars-segma/app/ /mnt/nars-segma/tests/
		docker run --rm -v "$$(pwd):/mnt" $(RUFF_IMAGE) format --check /mnt/nars-infra/scripts/ /mnt/nars-segma/app/ /mnt/nars-segma/tests/
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
	@echo "→ Checking undefined variable references (all targets)..."
	# GNUMAKEFLAGS is a GNU Make internal variable spuriously flagged by
	# --warn-undefined-variables -Rr (make 4.4+); FILE is the documented
	# make-argument of db-backup/db-restore (Usage: make db-restore FILE=...).
	# Capture into a variable and test on it, rather than on the pipeline exit
	# code: a pipeline ends with `grep -v`'s status, so if the only warnings were
	# filtered out entirely the old form falsely reported success.
	@undef=$$(for t in $$(make -qp 2>/dev/null | awk -F: '/^[a-zA-Z0-9_.%-]+:([^=]|$$)/ {print $$1}' | sort -u); do \
		make -Rr --warn-undefined-variables -n "$$t" 2>&1; \
	done | grep -i 'warning.*undefined' | grep -v GNUMAKEFLAGS | grep -v "'FILE'" || true); \
	if [ -n "$$undef" ]; then \
		echo "✖ Undefined variable references found (see above)"; \
		echo "$$undef"; \
		exit 1; \
	else \
		echo "✓ No undefined variable references"; \
	fi

.PHONY: infra-lint-checkmake
infra-lint-checkmake: ## Lint the root Makefile with checkmake (config: checkmake.ini)
	@if command -v checkmake >/dev/null 2>&1; then
		checkmake --config=checkmake.ini Makefile
	else
		docker run --rm -v "$$(pwd):/mnt:ro" --entrypoint /checkmake \
			$(CHECKMAKE_IMAGE) --config=/mnt/checkmake.ini /mnt/Makefile
	fi

.PHONY: infra-lint-sql
infra-lint-sql: ## Syntax-check migration/baseline/seed SQL with sqlfluff (postgres dialect)
# Only files WITHOUT psql meta-commands (\c, \gexec, ...) belong in this list —
# sqlfluff is a SQL parser, not a psql meta-command interpreter. That rules out
# scripts/create_nars_db.sql, which relies on \gexec/\c.
	@if [ -z "$(MIGRATIONS_SQL)" ]; then echo "✓ No SQL files to check"; exit 0; fi
	@if command -v sqlfluff >/dev/null 2>&1; then \
		for f in $(MIGRATIONS_SQL); do \
			echo "→ sqlfluff parse $$f"; \
			sqlfluff parse --dialect postgres "$$f" >/dev/null || exit 1; \
		done \
	else \
		for f in $(MIGRATIONS_SQL_MNT); do \
			echo "→ sqlfluff parse $$f"; \
			docker run --rm -v "$$(pwd):/mnt" $(SQLFLUFF_IMAGE) \
				parse --dialect postgres "$$f" >/dev/null || exit 1; \
		done \
	fi
	@echo "→ sqlfluff parse docs/seed_reference_data.sql (statements only)..."
	# The seed file is a psql COPY data dump: each block is
	# "COPY ... FROM stdin;" followed by tab-separated data rows until a
	# lone "\." terminator. The data rows are NOT SQL, so strip every data
	# block and parse the remaining statements (transaction, guards, TRUNCATE,
	# VACUUM ANALYZE) with the postgres dialect.
	@_tmp=$$(mktemp); trap 'rm -f "$$_tmp"' EXIT; \
	awk 'BEGIN{skip=0} /^COPY /{skip=1} skip{ if (/^\\\.$$/) skip=0; next } {print}' docs/seed_reference_data.sql > "$$_tmp"; \
	if command -v sqlfluff >/dev/null 2>&1; then \
		sqlfluff parse --dialect postgres "$$_tmp" >/dev/null; \
	else \
		docker run --rm -i -v "$$(pwd):/mnt:ro" $(SQLFLUFF_IMAGE) parse --dialect postgres - < "$$_tmp" >/dev/null; \
	fi \
	|| { echo "✖ docs/seed_reference_data.sql failed to parse (statements only)"; exit 1; }
	@echo "✓ SQL syntax OK ($(MIGRATIONS_SQL) docs/seed_reference_data.sql)"

.PHONY: infra-lint-nginx
infra-lint-nginx: ## Validate nginx frontend config with `nginx -t`
# The config points at cluster-internal DNS (resolver kube-dns..., upstream
# nars-api.nars.svc.cluster.local). A bare `nginx -t` on a host cannot resolve
# those, so this gate runs docker-only: `--add-host` sandbox mappings let the
# cluster names resolve while still catching all real syntax/structure errors.
	@command -v docker >/dev/null 2>&1 || { echo "✖ infra-lint-nginx needs docker"; exit 1; }
	@if ! docker image inspect $(NGINX_IMAGE) >/dev/null 2>&1; then docker pull $(NGINX_IMAGE) >/dev/null; fi
	@docker run --rm \
		--add-host "kube-dns.kube-system.svc.cluster.local.:127.0.0.11" \
		--add-host "nars-api.nars.svc.cluster.local:127.0.0.11" \
		--add-host "otel-collector.observability.svc.cluster.local:11.1.1.1" \
		-v "$$(pwd):/mnt" $(NGINX_IMAGE) sh -c \
		'cp /mnt/$(NGINX_CONF) /etc/nginx/conf.d/default.conf && \
		 mkdir -p /etc/nginx/snippets && \
		 cp /mnt/$(NGINX_SNIPPET) /etc/nginx/snippets/proxy-common.conf && \
		 nginx -t'
	@echo "✓ nginx configuration valid"


# Internal: warn when the mutable 'latest' tag is in use (build/push/load).
# Internal: reject IMAGE_TAG containing characters outside the whitelist
# (alphanumeric, dots, hyphens, underscores) before it is interpolated into a
# shell command. Shared by the build/push/apply tag gates below.
.PHONY: _check-tag-syntax
_check-tag-syntax:
	@if $(_check_tag_cmd); then \
		echo '✖ IMAGE_TAG='$(IMAGE_TAG_Q)' contains invalid characters (only alphanumeric, dots, hyphens, underscores allowed)'; \
		exit 1; \
	fi

.PHONY: _warn-latest-tag
_warn-latest-tag: _check-tag-syntax
	@if echo $(IMAGE_TAG_Q) | grep -qi "^latest$$"; then \
		echo "  ⚠ IMAGE_TAG=latest — set IMAGE_TAG=<commit-sha> for CI/CD builds"; \
	fi

.PHONY: _check-pinned-tag
_check-pinned-tag: _check-tag-syntax ## Fail if deploying with the mutable 'latest' tag outside local dev
	@if [ "$(ALLOW_LATEST)" != "1" ] && [ "$(DEPLOY_ENV)" != "dev" ] && echo $(IMAGE_TAG_Q) | grep -qi "^latest$$"; then
		echo "✖ Refusing to deploy IMAGE_TAG=latest in $(DEPLOY_ENV) — mutable tags break reproducible deployments.";
		echo "  Set IMAGE_TAG=<commit-sha>, or DEPLOY_ENV=dev, or ALLOW_LATEST=1 to override.";
		exit 1;
	fi

.PHONY: _check-local-ingresses
_check-local-ingresses: $(if $(filter-out dev,$(DEPLOY_ENV)),$(KUSTOMIZE_MANIFEST)) ## Fail if dev-only local ingresses would be deployed outside local dev
	@if [ "$(DEPLOY_ENV)" != "dev" ]; then
		if grep -qE "name: nars-(api|frontend)-local" "$(KUSTOMIZE_MANIFEST)"; then
			echo "✖ Refusing to deploy dev-only local ingresses (nars-api-local / nars-frontend-local) in $(DEPLOY_ENV).";
			echo "  They expose /api and /login WITHOUT mTLS and match any Host.";
			echo "  Ensure the overlay used ($(K8S_OVERLAY_DIR)) does not include ingress-local, or set DEPLOY_ENV=dev.";
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

.PHONY: infra-lint-digest-guard
infra-lint-digest-guard: ## Assert kustomize-digest-rewrite pins images and fails closed (self-test)
# All steps are offline: they exercise the awk rewriter and the
# resolve-image-digests.sh parser against fixtures (the script's
# INSPECT_JSON_FILE test seam), never a real registry.
	@tmp=$$(mktemp -d); trap 'rm -rf "$$tmp"' EXIT; \
	echo "→ Preparing digest-rewrite fixture..."; \
	printf 'nars-api sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nnars-tiles sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n' > "$$tmp/digests"; \
	printf '  image: zuhirbenslama/nars-api:sha-abc\n  image: zuhirbenslama/nars-tiles:sha-abc\n' > "$$tmp/in"; \
	awk -v org=zuhirbenslama -v images="nars-api nars-tiles" -v digest_file="$$tmp/digests" \
		-f "$(SCRIPTS_DIR)/kustomize-digest-rewrite.awk" < "$$tmp/in" > "$$tmp/out"; \
	if ! grep -q 'zuhirbenslama/nars-api@sha256:aaa' "$$tmp/out" || ! grep -q 'zuhirbenslama/nars-tiles@sha256:bbb' "$$tmp/out"; then \
		echo '✖ digest rewrite did not produce @sha256 pins'; cat "$$tmp/out"; exit 1; \
	fi; \
	if grep -q ':sha-abc' "$$tmp/out"; then \
		echo '✖ digest rewrite left a bare ":tag" reference behind'; exit 1; \
	fi; \
	echo "  ✓ images rewritten to @sha256 digests"; \
	echo "→ Verifying fail-closed when a digest is missing..."; \
	printf 'nars-api sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n' > "$$tmp/digests2"; \
	if awk -v org=zuhirbenslama -v images="nars-api nars-tiles" -v digest_file="$$tmp/digests2" \
		-f "$(SCRIPTS_DIR)/kustomize-digest-rewrite.awk" < "$$tmp/in" >/dev/null 2>&1; then \
		echo '✖ digest rewrite passed with a missing digest — must fail closed'; exit 1; \
	fi; \
	echo "  ✓ missing digest rejected (fail closed)"; \
	echo "→ Verifying already-digested references are left untouched..."; \
	printf '  image: zuhirbenslama/nars-api@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\n' > "$$tmp/in2"; \
	awk -v org=zuhirbenslama -v images="nars-api nars-tiles" -v digest_file="$$tmp/digests" \
		-f "$(SCRIPTS_DIR)/kustomize-digest-rewrite.awk" < "$$tmp/in2" > "$$tmp/out2"; \
	grep -q '@sha256:cccc' "$$tmp/out2" || { echo '✖ digested reference was unexpectedly modified'; exit 1; }; \
	echo "  ✓ digested reference left intact"; \
	echo "→ Verifying resolve-image-digests.sh parses docker manifest inspect --verbose output..."; \
	printf '[{"Ref":"docker.io/zuhirbenslama/nars-api:sha-abc","Descriptor":{"mediaType":"application/vnd.oci.image.index.v1+json","digest":"sha256:f00d","size":123},"SchemaV2Manifest":{}}]' > "$$tmp/inspect.json"; \
	out=$$(INSPECT_JSON_FILE="$$tmp/inspect.json" "$(SCRIPTS_DIR)/resolve-image-digests.sh" zuhirbenslama sha-abc nars-api); \
	printf '%s\n' "$$out" | grep -q '^nars-api sha256:f00d$$' || { echo "✖ resolver parsed the wrong digest: $$out"; exit 1; }; \
	echo "  ✓ resolver extracts the manifest digest"

# Internal: watch what CIDRs the health ingress would ship to a non-dev
# deployment. The base carries kind's defaults (k8s/ingress-api.yaml) for local
# clusters; the production overlay is required to replace them (overlays/
# production/patches/health-ingress.yaml), and secrets-validate fails closed
# while a REPLACE_ME_* placeholder remains. This guard is the belt-and-
# suspenders for the case where someone edits the production patch to lint-clean
# but still kind-default CIDRs — identical defaults would otherwise deploy to
# production silently. KIND_CIDR_MANIFEST is overridable so the self-test below
# can exercise the reject path against a fixture without touching the real
# render (defaults to the shared kustomize render, defined in make/deploy.mk).
# NOTE: the guard's prerequisite must stay $(KUSTOMIZE_MANIFEST) — overriding
# that variable also relocates its $(KUSTOMIZE_MANIFEST): FORCE build rule in
# deploy.mk, so a fixture would be OVERWRITTEN by the real render, silently
# making the fixture test vacuous. Only KIND_CIDR_MANIFEST (the file the guard
# greps) may be pointed at a fixture.
KIND_CIDR_MANIFEST ?= $(KUSTOMIZE_MANIFEST)

.PHONY: _check-kind-cidrs
_check-kind-cidrs: $(if $(filter-out dev,$(DEPLOY_ENV)),$(KUSTOMIZE_MANIFEST)) ## Fail if kind-default CIDRs would be deployed outside local dev
	@if [ "$(DEPLOY_ENV)" != "dev" ]; then
		if grep -qE "10\.244\.0\.0/16|10\.96\.0\.0/12" "$(KIND_CIDR_MANIFEST)"; then
			echo "✖ Refusing to deploy kind-default CIDRs (10.244.0.0/16 pod / 10.96.0.0/12 svc) in $(DEPLOY_ENV).";
			echo "  The health ingress must be restricted to the real cluster's pod/service CIDRs.";
			echo "  Edit $(K8S_OVERLAY_DIR)/patches/health-ingress.yaml and set";
			echo "  REPLACE_ME_POD_CIDR / REPLACE_ME_SVC_CIDR to your cluster's actual ranges.";
			exit 1;
		fi
	fi

.PHONY: infra-lint-local-ingress-guard
infra-lint-local-ingress-guard: ## Assert local ingresses are excluded from non-dev overlays (self-test)
	@echo "→ Verifying dev overlay actually ships the local ingresses (nars-api-local)..."
	@if ! $(KUBECTL) kustomize "$(K8S_OVERLAY_DIR)" 2>/dev/null | grep -qE "name: nars-(api|frontend)-local"; then
		echo "✖ dev overlay is missing dev local ingresses — 'make cluster-up' would lose localhost access";
		exit 1;
	fi
	@echo "  ✓ dev overlay includes local ingresses"
	@echo "→ Verifying _check-local-ingresses passes in production (prod overlay has no local ingresses)..."
	@DEPLOY_ENV=production $(SUBMAKE) _check-local-ingresses
	@echo "  ✓ production overlay rejects/times-out all local ingress output"
	@echo "→ Verifying _check-local-ingresses passes in dev..."
	@DEPLOY_ENV=dev $(SUBMAKE) _check-local-ingresses
	@echo "  ✓ local ingresses allowed in dev"

.PHONY: infra-lint-kind-cidr-guard
infra-lint-kind-cidr-guard: ## Assert _check-kind-cidrs rejects kind-default CIDRs outside dev (self-test)
# Fixtures override KIND_CIDR_MANIFEST (the file the guard greps) as make
# COMMAND-LINE variables so the reject path is exercised without depending on
# the real render. The $(KUSTOMIZE_MANIFEST) prerequisite still builds the real
# production overlay under DEPLOY_ENV=production — a side effect shared with the
# other guard self-tests, and step 3 below asserts that render separately.
	@echo "→ Verifying _check-kind-cidrs rejects kind-default CIDRs in production..."
	@_kind=$$(mktemp); _placeholder=$$(mktemp); trap 'rm -f "$$_kind" "$$_placeholder"' EXIT; \
	printf '%s\n' '      nginx.ingress.kubernetes.io/whitelist-source-range: "10.244.0.0/16,10.96.0.0/12"' > "$$_kind"; \
	printf '%s\n' '      nginx.ingress.kubernetes.io/whitelist-source-range: "REPLACE_ME_POD_CIDR,REPLACE_ME_SVC_CIDR"' > "$$_placeholder"; \
	if DEPLOY_ENV=production $(SUBMAKE) _check-kind-cidrs KIND_CIDR_MANIFEST="$$_kind" >/dev/null 2>&1; then
		echo "✖ _check-kind-cidrs unexpectedly accepted kind-default CIDRs in production";
		exit 1;
	fi
	@echo "  ✓ kind-default CIDRs rejected in production"
	@echo "→ Verifying the CIDR guard stays orthogonal to REPLACE_ME (that is secrets-validate's gate)..."
	@if ! DEPLOY_ENV=production $(SUBMAKE) _check-kind-cidrs KIND_CIDR_MANIFEST="$$_placeholder" >/dev/null 2>&1; then
		echo "✖ _check-kind-cidrs rejected an unedited REPLACE_ME manifest — it must stay orthogonal to secrets-validate";
		exit 1;
	fi
	@echo "  ✓ unedited REPLACE_ME manifest passes the CIDR guard (secrets-validate owns that gate)"
	@echo "→ Verifying the real production render ships no kind-default CIDRs..."
	@echo "  (prerequisite of _check-kind-cidrs renders the production overlay and greps the shared manifest)"
	@DEPLOY_ENV=production $(SUBMAKE) _check-kind-cidrs
	@echo "  ✓ production overlay eliminates the kind-default CIDRs"
	@echo "→ Verifying _check-kind-cidrs passes in dev (kind defaults are fine locally)..."
	@DEPLOY_ENV=dev $(SUBMAKE) _check-kind-cidrs
	@echo "  ✓ kind-default CIDRs allowed in dev"

.PHONY: infra-lint-observability-security
infra-lint-observability-security: ## Assert _check-observability-security rejects insecure Helm values in production (self-test)
	@echo "→ Verifying production observability security check rejects insecure Helm values..."
	@if DEPLOY_ENV=production $(SUBMAKE) _check-observability-security >/dev/null 2>&1; then
		echo "✖ _check-observability-security unexpectedly passed with insecure defaults in production";
		exit 1;
	fi
	@echo "  ✓ insecure defaults rejected in production"
	@echo "→ Verifying _check-observability-security warns (skips) in dev..."
	@DEPLOY_ENV=dev $(SUBMAKE) _check-observability-security
	@echo "  ✓ dev skips observability security check"

.PHONY: infra-lint-markdown
infra-lint-markdown: ## Lint docs markdown with markdownlint (style: nars-infra/.markdownlint.rb)
	@if [ -z "$(DOCS_MD)" ]; then echo "✓ No markdown files to check"; exit 0; fi
	@if command -v mdl >/dev/null 2>&1; then
		mdl -s nars-infra/.markdownlint.rb $(DOCS_MD)
	else
		docker run --rm -v "$$(pwd):/mnt:ro" $(MARKDOWNLINT_IMAGE) \
			-s /mnt/nars-infra/.markdownlint.rb $(DOCS_MD_MNT)
	fi

# Internal: assert docs/uml/*.md class diagrams are in sync with their source
# trees. docs-lint-uml proves only that the mermaid parses; this gate proves
# every type and member listed in the backend class diagram exists in nars-api
# and the vite component diagram's classes/members resolve in nars-web (they
# previously missed a refactor that moved lockout out of RefreshTokenService
# and inspection/entrance out of FieldService).
.PHONY: infra-lint-uml-drift
infra-lint-uml-drift: ## Assert UML class diagrams' types/members exist in nars-api + nars-web (drift guard)
	@command -v python3 >/dev/null 2>&1 || { echo "✖ python3 is not installed (required for infra-lint-uml-drift)"; exit 1; }
	@python3 nars-infra/scripts/check_uml_class_diagram.py
	@python3 nars-infra/scripts/check_uml_vite_component_diagram.py

# Internal: assert the migrations/ DDL and the Docker-init schema
# (create_nars_db.sql §10) cannot drift apart. The migration file itself
# documents the trap: both files create ai_draft_features, and "divergent
# index/constraint names silently create duplicates instead of no-oping"
# (`CREATE INDEX IF NOT EXISTS`/`ADD CONSTRAINT IF NOT EXISTS` match by NAME).
# This gate mechanizes that warning the way infra-lint-uml-drift mechanizes the
# diagram-drift warning — a rename in one file without the other now fails CI
# instead of silently double-creating objects on a real database.
.PHONY: infra-lint-migration-drift
infra-lint-migration-drift: ## Assert create_nars_db.sql and migrations/*.sql define identical ai_draft objects (drift guard)
	@command -v python3 >/dev/null 2>&1 || { echo "✖ python3 is not installed (required for infra-lint-migration-drift)"; exit 1; }
	@python3 nars-infra/scripts/check_migration_drift.py

# Local gate for the / vs /map bundle-sync guard (see check_frontend_bundle_sync.py).
# Runs only when a fresh local dist exists; the deploy-time _check-bundle-sync and
# the NarsApi.WwwrootAssetSyncTests cover the committed/CI and live-cluster paths.
.PHONY: infra-lint-frontend-wwwroot-sync
infra-lint-frontend-wwwroot-sync: ## Assert a local nars-web build stays in sync with nars-api/wwwroot (drift guard)
	@if [ -f nars-web/dist/index.html ] && [ -f nars-api/wwwroot/index.html ]; then \
		python3 nars-infra/scripts/check_frontend_bundle_sync.py --frontend nars-web/dist/index.html --api nars-api/wwwroot/index.html; \
	else \
		echo "  ↷ nars-web/dist missing — skipping (deploy-time bundle-sync check still applies)"; \
	fi

# Self-test for the images-build content-stamp machinery (make/scripts/
# image-hash-guard.py + __image_guard + the six _build-nars-* recipes).
# Guards the guard: the arg-order bug that shipped the stamp into a repo-root
# Dockerfile-named file and made every build a rebuild would otherwise go
# undetected until images-build behaves oddly. Pins the helper's contract —
# exit 0 = rebuild, exit 1 = skip, git-ignored paths never hash, and all six
# recipes must pass the stamp as the first __image_guard argument — so the
# recipes, the helper, and CI's paths-filter cannot silently disagree.
.PHONY: infra-lint-image-guard
infra-lint-image-guard: ## Assert the images-build content-stamp guard skips unchanged images and rebuilds on real change (self-test)
	@echo "→ Verifying all 6 _build-nars-* recipes pass the stamp as __image_guard's first argument..."
	@count=$$(grep -cE '__image_guard "[^"]*st\.guard' make/images.mk); \
	if [ "$$count" -ne 6 ]; then \
		echo "✖ expected 6 __image_guard stamp-first invocations, found $$count"; \
		exit 1; \
	fi
	@echo "  ✓ stamp-first argument order locked"
	@echo "→ Verifying image-hash-guard.py skips unchanged sources but rebuilds on real change..."
	@_fixture=$$(mktemp -d); trap 'rm -rf "$$_fixture"' EXIT; \
	mkdir -p "$$_fixture/src" "$$_fixture/artifacts" "$$_fixture/.image-hashes"; \
	printf 'v1\n' > "$$_fixture/src/app.txt"; \
	printf 'artifact-1\n' > "$$_fixture/artifacts/gen.txt"; \
	cd "$$_fixture"; \
	git init -q; \
	printf 'artifacts/\n' > .gitignore; \
	git -c user.name=guard -c user.email=guard@test add -A; \
	git -c user.name=guard -c user.email=guard commit -qm init; \
	stamp="$$_fixture/.image-hashes/app.guard"; \
	if python3 "$(CURDIR)/make/scripts/image-hash-guard.py" "$$stamp" 'src/**' 'artifacts/**'; then \
		echo "  ✓ first build: no stamp → rebuild (exit 0)"; \
	else \
		echo "✖ first build must return 0 (rebuild)"; exit 1; \
	fi	; \
	if python3 "$(CURDIR)/make/scripts/image-hash-guard.py" "$$stamp" 'src/**' 'artifacts/**'; then \
		echo "✖ unchanged sources must return 1 (skip)"; exit 1; \
	else \
		echo "  ✓ unchanged sources → skip (exit 1)"; \
	fi	; \
	printf 'artifact-2\n' > "$$_fixture/artifacts/gen.txt"; \
	if python3 "$(CURDIR)/make/scripts/image-hash-guard.py" "$$stamp" 'src/**' 'artifacts/**'; then \
		echo "✖ git-ignored artifact change must return 1 (skip)"; exit 1; \
	else \
		echo "  ✓ git-ignored artifact churn → skip (paths-filter lockstep)"; \
	fi	; \
	printf 'v2\n' > "$$_fixture/src/app.txt"; \
	if python3 "$(CURDIR)/make/scripts/image-hash-guard.py" "$$stamp" 'src/**' 'artifacts/**'; then \
		echo "  ✓ tracked source change → rebuild (exit 0)"; \
	else \
		echo "✖ tracked source change must return 0 (rebuild)"; exit 1; \
	fi	; \
	if [ -f "$$stamp" ] && grep -qE '^[0-9a-f]{64}$$' "$$stamp"; then \
		echo "  ✓ stamp persisted at $$stamp"; \
	else \
		echo "✖ stamp file missing or not a sha256 digest at $$stamp"; exit 1; \
	fi

# Server-side validation of the rendered manifests. A plain `kubectl kustomize`
# parse cannot catch the class of bug the nvidia.com/gpu sibling-placement was:
# the YAML is well-formed, only the API server rejects it ("strict decoding
# error: unknown field"). This gate renders the overlay, boots a THROWAWAY kind
# cluster, REALLY applies the admission scaffold (Namespace + LimitRange +
# ResourceQuota + PV — the namespace must exist for namespaced objects to be
# admitted, and keeping the quota/limit objects present shapes admission like
# production), then dry-runs the remainder server-side. Heavier than the other
# gates (it spawns a cluster), so it is deliberately NOT part of `make
# infra-lint` — CI runs it as its own job.
.PHONY: infra-lint-k8s-dry-run
infra-lint-k8s-dry-run: ## Server-side dry-run of the rendered manifests against a throwaway kind cluster (CI gate)
	@command -v $(KIND) >/dev/null 2>&1 || { echo "✖ $(KIND) is not installed → https://kind.sigs.k8s.io/docs/user/quick-start/"; exit 1; }
	@command -v python3 >/dev/null 2>&1 || { echo "✖ python3 is not installed (required to split the render)"; exit 1; }
	@echo "→ Server-side dry-run of $(K8S_OVERLAY_DIR) on a throwaway kind cluster..."
	@set -e; \
	tmpdir=$$(mktemp -d); \
	cluster="nars-dryrun-$$$$"; \
	kctx="kind-$$cluster"; \
	trap '$(KIND) delete cluster --name "$$cluster" >/dev/null 2>&1 || true; rm -rf "$$tmpdir"' EXIT; \
	echo "→ Creating throwaway kind cluster '$$cluster'..."; \
	$(KIND) create cluster --name "$$cluster" >/dev/null; \
	$(KUBECTL) --context "$$kctx" wait --for=condition=Ready node/$$cluster-control-plane --timeout=180s >/dev/null; \
	echo "→ Rendering $(K8S_OVERLAY_DIR)..."; \
	$(KUBECTL) kustomize "$(K8S_OVERLAY_DIR)" > "$$tmpdir/render.yaml"; \
	$(KUBECTL) --context "$$kctx" apply --dry-run=client -o json -f "$$tmpdir/render.yaml" > "$$tmpdir/render.json"; \
	python3 nars-infra/scripts/split_k8s_render.py "$$tmpdir/render.json" "$$tmpdir/scaffold.json" "$$tmpdir/rest.json"; \
	echo "→ Applying admission scaffold (namespace + limitrange + quota + PV)..."; \
	$(KUBECTL) --context "$$kctx" apply -f "$$tmpdir/scaffold.json" >/dev/null; \
	echo "→ Server-side dry-run of the remaining render objects..."; \
	$(KUBECTL) --context "$$kctx" apply --dry-run=server -f "$$tmpdir/rest.json" >/dev/null; \
	echo "  ✓ $(K8S_OVERLAY_DIR) accepted by the API server"; \
	echo "→ Self-test: the historical unknown-field bug must be rejected..."; \
	cat > "$$tmpdir/bad-unknown-field.yaml" <<-'BADEOF'
	apiVersion: apps/v1
	kind: Deployment
	metadata:
	  name: nars-segma
	  namespace: nars
	spec:
	  selector:
	    matchLabels:
	      app: nars-segma
	  template:
	    metadata:
	      labels:
	        app: nars-segma
	    spec:
	      containers:
	        - name: nars-segma
	          image: zuhirbenslama/nars-segma:latest
	          resources:
	            resources.nvidia.com/gpu: "1"
	BADEOF
	if $(KUBECTL) --context "$$kctx" apply --dry-run=server -f "$$tmpdir/bad-unknown-field.yaml" >/dev/null 2>&1; then \
		echo "✖ server-side dry-run unexpectedly accepted resources.nvidia.com/gpu as a sibling of limits/requests"; \
		exit 1; \
	fi; \
	echo "  ✓ unknown-field bug rejected by the API server"
	@echo "✓ $(K8S_OVERLAY_DIR) validated server-side on a real API server"
