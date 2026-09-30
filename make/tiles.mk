# Included by the top-level Makefile. Satellite-imagery tile server targets
# (nginx-served XYZ pyramid + MapServer WMS via fcgiwrap; see
# nars-infra/docker/Dockerfile.nars-tiles).


# GDAL image used for pyramid synthesis (data prep runs here, not in the tile
# server image — that one stays small and never carries GDAL).
TILES_GDAL_IMAGE ?= ghcr.io/osgeo/gdal:ubuntu-small-3.10.3

# Optional live-request gate for build_imagery_pyramid.sh. The script refuses
# to run without it as a written consent that the input imagery licence permits
# redistribution + derivative caching (Copyright reminders are worth repeating).
CONFIRM_IMAGERY_LICENCE ?=

# build_imagery_pyramid.sh inputs (caller-supplied; declared so the infra
# makefile gate's --warn-undefined-variables stays clean while the script's own
# ${VAR:-default} handling remains the single owner of the defaults).
IMAGERY_INPUT ?=
AREAS_GPKG ?=
AREAS_LAYER ?=
MIN_ZOOM ?=
MAX_ZOOM ?=
THREADS ?=
STAGE_DIR ?=
TILES_DATA_DIR ?=
TILES_ACTIVATE ?=

.PHONY: tiles-smoke-test
tiles-smoke-test: ## Build the tile server image, synthesize a tiny world pyramid, and run the end-to-end static-tile/WMS assertions
	@TILES_GDAL_IMAGE="$(TILES_GDAL_IMAGE)" ./nars-infra/scripts/tiles_smoke_test.sh

.PHONY: tiles-pyramid-build
tiles-pyramid-build: ## Clip IMAGERY_INPUT to AREAS_GPKG, build the z-pyramid into $(TILES_DATA_DIR)/tiles, gate coverage, and (only with TILES_ACTIVATE=1) rotate it into place
	@test -n "$(CONFIRM_IMAGERY_LICENCE)" || { \
		echo "✖ refusing to run without CONFIRM_IMAGERY_LICENCE=1 — confirm the imagery licence permits "; \
		echo "  redistribution + derivative caching (see AGENTS.md data-safety rules)."; \
		exit 1; \
	}
	@test -f "$(IMAGERY_INPUT)" || { echo "✖ IMAGERY_INPUT=$(IMAGERY_INPUT) is not a file"; exit 1; }
	@test -f "$(AREAS_GPKG)" || { echo "✖ AREAS_GPKG=$(AREAS_GPKG) is not a file"; exit 1; }
	@TILES_GDAL_IMAGE="$(TILES_GDAL_IMAGE)" \
		IMAGERY_INPUT="$(IMAGERY_INPUT)" \
		AREAS_GPKG="$(AREAS_GPKG)" \
		AREAS_LAYER="$(AREAS_LAYER)" \
		MIN_ZOOM="$(MIN_ZOOM)" \
		MAX_ZOOM="$(MAX_ZOOM)" \
		THREADS="$(THREADS)" \
		STAGE_DIR="$(STAGE_DIR)" \
		TILES_DATA_DIR="$(TILES_DATA_DIR)" \
		TILES_ACTIVATE="$(TILES_ACTIVATE)" \
		./nars-infra/scripts/build_imagery_pyramid.sh