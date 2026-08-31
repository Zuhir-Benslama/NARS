# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: regeneration of generated docs artifacts (mermaid UML -> PDF).

# The UML diagrams are authored as ```mermaid blocks in docs/uml/*.md and
# shipped as docs/pdf/nars-uml-diagrams.pdf. That PDF is BUILT ARTIFACT and
# drifts whenever a diagram source is edited but not re-rendered (this bit the
# repo once: the class diagram was edited to drop OwnsFeatureAsync/dead
# UserProfileService methods, but the committed PDF was 10 days stale).
#
# The pipeline is manual-only upstream (render-mermaid-playwright.mjs +
# png-to-pdf.py). This target wraps it so regeneration is one repeatable,
# documented command instead of an undocumented chore.
#
# Dependencies (checked by the target):
#   - node + Playwright (Firefox) resolvable from nars-web/package.json
#     (the render script imports playwright; install via `npm ci` in nars-web)
#   - network access to a mermaid@11 CDN (jsdelivr, else unpkg)
#   - Pillow (png-to-pdf.py) and pdfunite (PDF merge) — both present here
#   - a writable build dir (default /tmp/nars/uml-build)

UML_SRC_DIR      ?= docs/uml
UML_BUILD_DIR    ?= $(LOG_DIR)/uml-build
UML_PDF_OUT      ?= docs/pdf/nars-uml-diagrams.pdf

.PHONY: docs-uml-pdf
docs-uml-pdf: ## Regenerate docs/pdf/nars-uml-diagrams.pdf from docs/uml/*.md (mermaid -> PNG -> PDF)
	@echo "→ Rendering UML diagrams to PNG (needs node + Playwright Firefox + mermaid CDN)..."
	@command -v node >/dev/null 2>&1 || { echo "✖ node is not installed"; exit 1; }
	@if ! node -e "require('playwright')" >/dev/null 2>&1 && \
	    ! node -e "const {createRequire}=require('node:module');createRequire(require('path').resolve('nars-web/package.json'))('playwright')" >/dev/null 2>&1; then \
		echo "✖ Playwright (Firefox) is not resolvable from nars-web/ — run 'npm ci' in nars-web first."; \
		exit 1; \
	fi
	@rm -rf "$(UML_BUILD_DIR)"; mkdir -p "$(UML_BUILD_DIR)"
	@node nars-infra/scripts/render-mermaid-playwright.mjs \
		"$(UML_SRC_DIR)" "$(UML_BUILD_DIR)"
	@echo "→ Converting PNGs to per-diagram PDFs..."
	@python3 nars-infra/scripts/png-to-pdf.py "$(UML_BUILD_DIR)"
	@echo "→ Merging diagrams into $(UML_PDF_OUT)..."
	@pdfunite "$(UML_BUILD_DIR)"/*.pdf "$(UML_PDF_OUT)"
	@echo "✓ $(UML_PDF_OUT) regenerated from $(UML_SRC_DIR)"
