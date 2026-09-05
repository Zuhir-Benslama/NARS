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
# methods).
#
# The technical report is authored in docs/nars_documentation.tex.
# docs-tex-pdf runs two pdflatex passes (for TOC/cref resolution).
#
# Dependencies (checked by each target):
#   docs-uml-pdf: node + Playwright (Firefox) from nars-web/ + mermaid@11 CDN
#   docs-tex-pdf: pdflatex (texlive)

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
