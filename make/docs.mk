# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: regeneration of generated docs artifacts (mermaid UML -> PDF, LaTeX -> PDF).
#
# Both PDFs under docs/pdf/ are gitignored build artifacts. Regenerate
# locally before sharing.
#
# UML diagrams are authored as ```mermaid blocks in docs/uml/*.md.
# The pipeline is manual-only upstream (render-mermaid-playwright.mjs +
# png-to-pdf.py). docs-uml-pdf wraps it so regeneration is one repeatable,
# documented command (previously the committed PDF drifted 10 days behind a
# class-diagram edit that removed OwnsFeatureAsync / dead UserProfileService
# methods). `make docs-lint` is the CI gate for docs/: it renders every
# mermaid block (docs-lint-uml), lints the markdown (infra-lint-markdown), and
# checks the backend class diagram against nars-api source, so a deleted or
# renamed type/member fails CI instead of silently rotting
# (infra-lint-uml-drift).
#
# The technical report is authored in docs/nars_documentation.tex.
# docs-tex-pdf runs two pdflatex passes (for TOC/cref resolution).
#
# Dependencies (checked by each target):
#   docs-uml-pdf: node + Playwright (Firefox) from nars-web/ + mermaid@11 CDN
#   docs-tex-pdf: pdflatex (texlive)
#   docs-tex-lint: docker (digest-pinned texlive image)

UML_SRC_DIR      ?= docs/uml
UML_BUILD_DIR    ?= $(LOG_DIR)/uml-build
UML_PDF_OUT      ?= docs/pdf/nars-uml-diagrams.pdf

# Internal: node must exist and Playwright (Firefox) must be resolvable from
# nars-web/ (indirectly, since it is a dependency there), else both grooming
# targets fail fast with a clear message. Also run 'npm ci' in nars-web first.
.PHONY: _check-playwright
_check-playwright:
	@command -v node >/dev/null 2>&1 || { echo "✖ node is not installed"; exit 1; }
	@if ! node -e "require('playwright')" >/dev/null 2>&1 && \
	    ! node -e "const {createRequire}=require('node:module');createRequire(require('path').resolve('nars-web/package.json'))('playwright')" >/dev/null 2>&1; then \
		echo "✖ Playwright (Firefox) is not resolvable from nars-web/ — run 'npm ci' in nars-web first."; \
		exit 1; \
	fi

.PHONY: docs-lint
docs-lint: docs-lint-uml ## CI gate: render UML diagrams, lint docs markdown, check class-diagram drift
	$(SUBMAKE) infra-lint-markdown
	$(SUBMAKE) infra-lint-uml-drift

# CI gate: render every ```mermaid block under docs/uml and fail if any diagram
# does not render (or if the renderer finds no src / no diagrams). Shared with
# the docs-uml-pdf preflight so a broken diagram breaks the pipeline before it
# reaches the hand-off PDF regeneration path.
.PHONY: docs-lint-uml
docs-lint-uml: _check-playwright ## CI gate: validate all docs/uml/*.md mermaid diagrams render
	@rm -rf "$(UML_BUILD_DIR)"; mkdir -p "$(UML_BUILD_DIR)"
	@node nars-infra/scripts/render-mermaid-playwright.mjs \
		"$(UML_SRC_DIR)" "$(UML_BUILD_DIR)"
	@echo "✓ All UML diagrams under $(UML_SRC_DIR) rendered successfully"

.PHONY: docs-uml-pdf
docs-uml-pdf: _check-playwright ## Regenerate docs/pdf/nars-uml-diagrams.pdf from docs/uml/*.md (mermaid -> PNG -> PDF)
	@echo "→ Rendering UML diagrams to PNG (needs node + Playwright Firefox + mermaid CDN)..."
	@rm -rf "$(UML_BUILD_DIR)"; mkdir -p "$(UML_BUILD_DIR)"
	@node nars-infra/scripts/render-mermaid-playwright.mjs \
		"$(UML_SRC_DIR)" "$(UML_BUILD_DIR)"
	@echo "→ Converting PNGs to per-diagram PDFs..."
	@python3 nars-infra/scripts/png-to-pdf.py "$(UML_BUILD_DIR)"
	@echo "→ Merging diagrams into $(UML_PDF_OUT)..."
	@pdfunite "$(UML_BUILD_DIR)"/*.pdf "$(UML_PDF_OUT)"
	@echo "✓ $(UML_PDF_OUT) regenerated from $(UML_SRC_DIR)"

TEX_BUILD_DIR    ?= $(LOG_DIR)/tex-build
# Source is under docs/; the recipe runs from the repo root, so a bare
# filename would make pdflatex abort with a file error (and write no log,
# masking the cause). Keep the full path in one place.
TEX_SRC          := docs/nars_documentation.tex
TEX_PDF_LOG      := $(TEX_BUILD_DIR)/nars_documentation.log

# Tag for the docs-tex compile image (pushed to $(DOCKER_ORG)/nars-docs-tex).
# Bump on any base/package change; TEX_IMAGE (root Makefile) pins the released
# image by digest, so old digests keep resolving after a re-push.
DOCS_TEX_IMAGE_TAG ?= 0.1.0

# Extra `docker build` args for the docs-tex image. Empty for local one-shot
# builds; CI supplies BuildKit gha cache flags (via env) so the base pull and
# tlmgr-install layer are warmed across runs. Keeps the Makefile as the single
# owner of the build command while letting CI tune caching.
DOCS_TEX_DOCKER_BUILD_ARGS ?=

.PHONY: docs-tex-image
docs-tex-image: ## Build $(DOCKER_ORG)/nars-docs-tex:$(DOCS_TEX_IMAGE_TAG) (see nars-infra/docker/Dockerfile.nars-docs-tex)
	@docker build $(DOCS_TEX_DOCKER_BUILD_ARGS) \
		-f "$(DOCKER_DIR)/Dockerfile.nars-docs-tex" \
		-t "$(DOCKER_ORG)/nars-docs-tex:$(DOCS_TEX_IMAGE_TAG)" .

.PHONY: docs-tex-pdf
docs-tex-pdf: ## Regenerate docs/pdf/nars_documentation.pdf from docs/nars_documentation.tex (pdflatex, two passes)
	@echo "→ Building nars_documentation.tex (needs pdflatex)..."
	@command -v pdflatex >/dev/null 2>&1 || { echo "✖ pdflatex is not installed (install texlive)"; exit 1; }
	@rm -rf "$(TEX_BUILD_DIR)"; mkdir -p "$(TEX_BUILD_DIR)"
	@pdflatex -interaction=nonstopmode -halt-on-error -output-directory "$(TEX_BUILD_DIR)" \
		"$(TEX_SRC)" >/dev/null 2>&1 \
		|| { echo "✖ First pdflatex pass failed:"; tail -30 "$(TEX_PDF_LOG)"; exit 1; }
	@pdflatex -interaction=nonstopmode -output-directory "$(TEX_BUILD_DIR)" \
		"$(TEX_SRC)" >/dev/null 2>&1 \
		|| { echo "✖ Second pdflatex pass failed:"; tail -30 "$(TEX_PDF_LOG)"; exit 1; }
	@mkdir -p docs/pdf
	@cp "$(TEX_BUILD_DIR)/nars_documentation.pdf" docs/pdf/nars_documentation.pdf
	@echo "✓ docs/pdf/nars_documentation.pdf regenerated from docs/nars_documentation.tex"

# CI gate: prove the committed .tex compiles (2 passes). The tex source is
# committed but docs/pdf/*.pdf are gitignored build artifacts, so without a
# gate a LaTeX error or a source edit that breaks the build ships silently and
# the shipped PDF goes stale (see docs/code-review D4). Docs-lint-uml is the
# renderer gate for docs/uml/*.md; this is the equivalent for the report —
# compilation is proven in the pinned texlive image, while the actual PDF
# regen stays manual (docs-tex-pdf). Mounts the repo read-only at /mnt and
# runs from /mnt/docs so relative \includegraphics/\input keep their paths;
# build artifacts land in a host temp dir (kept on failure for the log).
.PHONY: docs-tex-lint
docs-tex-lint: ## CI gate: prove docs/nars_documentation.tex compiles (2 passes, pinned texlive image)
	@command -v docker >/dev/null 2>&1 || { echo "✖ docs-tex-lint needs docker"; exit 1; }
	@if ! docker image inspect $(TEX_IMAGE) >/dev/null 2>&1; then docker pull $(TEX_IMAGE) >/dev/null; fi
	@tmpdir=$$(mktemp -d); trap 'rm -rf "$$tmpdir"' EXIT; \
	if docker run --rm \
		-v "$$(pwd):/mnt:ro" \
		-v "$$tmpdir":/out -w /mnt/docs \
		--entrypoint sh \
		$(TEX_IMAGE) -c \
		'pdflatex -interaction=nonstopmode -halt-on-error -output-directory=/out nars_documentation.tex >/dev/null 2>&1 && \
		 pdflatex -interaction=nonstopmode -halt-on-error -output-directory=/out nars_documentation.tex >/dev/null 2>&1'; then \
		echo "✓ docs/nars_documentation.tex compiles (2 passes)"; \
	else \
		echo "✖ docs/nars_documentation.tex failed to compile — log:"; \
		tail -30 "$$tmpdir/nars_documentation.log" 2>/dev/null || true; \
		exit 1; \
	fi
