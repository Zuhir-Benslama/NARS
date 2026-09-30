#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# build_imagery_pyramid.sh — build/validate/activate the national satellite
# tile pyramid in a staging dir, keeping the served pyramid untouched until the
# new one passes coverage.
#
# ⚠  GATE: only run on imagery whose licence permits redistribution AND the
#    derivative tile pyramid (the imagery is re-encoded and re-served). Confirm
#    the delivery licence before the first ingest.
#
# Pipeline (all GDAL tools, run on the tile server or any box with GDAL >= 3.10):
#   1. VRT-mosaic the input rasters.
#   2. Clip to the commune AREAS union, crop-to-cutline, reproject EPSG:3857,
#      write a single urban GeoTIFF with overviews. This one file feeds BOTH
#      the static pyramid and the WMS tileindex.
#   3. gdal2tiles --xyz -r bilinear with --processes for the target zooms.
#   4. gdaltindex with ABSOLUTE paths (relative location fields silently fail
#      inside MapServer via msTryBuildPath3's CWD fallback).
#   5. check_tile_coverage.py against the staging tree. On ANY gap this script
#      exits non-zero and leaves the staging dir in place; the served pyramid
#      is never touched ("keep the previous pyramid until the new one passes").
#   6. TILES_ACTIVATE=1 moves the passed staging tree into $TILES_DATA_DIR,
#      rotating the previous tree into a dated backup. Nothing is ever deleted
#      by this script; prune backups yourself.
#
# Required env:
#   IMAGERY_INPUT   space-separated source GeoTIFFs (any CRS/SRID)
#   AREAS_GPKG      GPKG of commune polygons (see --layer)
# Optional env:
#   AREAS_LAYER     commune layer name in AREAS_GPKG   (default: communes)
#   MIN_ZOOM        first zoom level to tile           (default: 10)
#   MAX_ZOOM        zoom level checked by coverage     (default: 18)
#   THREADS         gdal2tiles --processes             (default: nproc)
#   STAGE_DIR       staging root                       (default: ./tiles-build)
#   TILES_DATA_DIR  activate target                    (default: /data)
#   TILES_ACTIVATE  1 to swap staging into $TILES_DATA_DIR (default: 0)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

# ── Configuration ────────────────────────────────────────────────────────────
IMAGERY_INPUT="${IMAGERY_INPUT:-}"
AREAS_GPKG="${AREAS_GPKG:-}"
AREAS_LAYER="${AREAS_LAYER:-communes}"
MIN_ZOOM="${MIN_ZOOM:-10}"
MAX_ZOOM="${MAX_ZOOM:-18}"
THREADS="${THREADS:-$(nproc)}"
STAGE_DIR="${STAGE_DIR:-$(pwd)/tiles-build}"
TILES_DATA_DIR="${TILES_DATA_DIR:-/data}"
TILES_ACTIVATE="${TILES_ACTIVATE:-0}"

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; YELLOW='\033[1;33m'; RESET='\033[0m'
info()    { echo -e "${CYAN}[INFO]${RESET}  $*"; }
success() { echo -e "${GREEN}[OK]${RESET}    $*"; }
warn()    { echo -e "${YELLOW}[WARN]${RESET}  $*"; }
die()     { echo -e "${RED}[ERROR]${RESET} $*" >&2; exit 1; }

for tool in gdalbuildvrt gdalwarp gdaladdo gdal2tiles.py gdaltindex python3; do
    command -v "${tool}" >/dev/null 2>&1 || die "${tool} not found — run inside the GDAL image or on the tile server"
done
[ -n "${IMAGERY_INPUT}" ] || die "IMAGERY_INPUT is required (space-separated source GeoTIFFs)"
[ -n "${AREAS_GPKG}" ] || die "AREAS_GPKG is required"
[ -f "${AREAS_GPKG}" ] || die "AREAS_GPKG not found: ${AREAS_GPKG}"
[ "${MIN_ZOOM}" -le "${MAX_ZOOM}" ] || die "MIN_ZOOM must be <= MAX_ZOOM"

# Anything already present in the staging dir is preserved (never clobbered).
SRC_STAGE="${STAGE_DIR}/sources"
TILE_STAGE="${STAGE_DIR}/tiles"
mkdir -p "${SRC_STAGE}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 1. Mosaic
MOSAIC="${STAGE_DIR}/mosaic.vrt"
info "Building mosaic VRT from: ${IMAGERY_INPUT}"
# IMAGERY_INPUT is intentionally split on whitespace (multiple sources).
# shellcheck disable=SC2086
gdalbuildvrt -q "${MOSAIC}" ${IMAGERY_INPUT}

# 2. Clip + reproject
URBAN_TIF="${SRC_STAGE}/national_urban.tif"
info "Clipping to ${AREAS_LAYER} union and reprojecting to EPSG:3857..."
ogr2ogr -q -f GPKG -nln areas_clip "${STAGE_DIR}/areas_clip.gpkg" "${AREAS_GPKG}" "${AREAS_LAYER}" \
    || die "cannot read layer '${AREAS_LAYER}' from ${AREAS_GPKG}"
gdalwarp -q -t_srs EPSG:3857 \
    -cutline "${STAGE_DIR}/areas_clip.gpkg" -crop_to_cutline \
    -r bilinear -overwrite \
    "${MOSAIC}" "${URBAN_TIF}"
success "urban GeoTIFF written (${URBAN_TIF})"

# 3. Overviews
info "Building overviews..."
gdaladdo -q -r average "${URBAN_TIF}" 2 4 8 16 32 64

# 4. Pyramid
info "gdal2tiles --xyz -r bilinear -z ${MIN_ZOOM}-${MAX_ZOOM} --processes ${THREADS}..."
gdal2tiles.py -p mercator -r bilinear --processes "${THREADS}" \
    -z "${MIN_ZOOM}-${MAX_ZOOM}" --xyz \
    "${URBAN_TIF}" "${TILE_STAGE}"

# 5. Tileindex with absolute paths (MapServer CWD fallback is a silent failure)
info "Building tileindex..."
gdaltindex -q -of GPKG -t_srs EPSG:3857 -tileindex location -write_absolute_path \
    "${SRC_STAGE}/tileindex.gpkg" \
    "$(cd "$(dirname "${URBAN_TIF}")" && pwd)/$(basename "${URBAN_TIF}")"
success "tileindex written (absolute location paths)"

# 6. Coverage gate
info "Checking z${MAX_ZOOM} coverage of the staging tree..."
if python3 "${SCRIPT_DIR}/check_tile_coverage.py" \
    --tileindex "${SRC_STAGE}/tileindex.gpkg" \
    --areas "${AREAS_GPKG}" \
    --layer "${AREAS_LAYER}" \
    --zoom "${MAX_ZOOM}"; then
    success "coverage check passed — staging tree is ready"
else
    warn "coverage check FAILED — staging tree kept at ${STAGE_DIR} for inspection"
    warn "the served pyramid was NOT touched"
    exit 1
fi

# 7. Activation (opt-in, never destructive)
if [ "${TILES_ACTIVATE}" != "1" ]; then
    warn "TILES_ACTIVATE != 1 — pyramid built but NOT activated."
    warn "review ${STAGE_DIR}, then re-run with TILES_ACTIVATE=1"
    exit 0
fi

if [ -d "${TILES_DATA_DIR}/tiles" ]; then
    BACKUP="${TILES_DATA_DIR}/previous_$(date +%Y%m%d_%H%M%S)"
    info "Rotating served tree into ${BACKUP}..."
    mkdir -p "${BACKUP}"
    mv "${TILES_DATA_DIR}/tiles" "${BACKUP}/tiles"
    mv "${TILES_DATA_DIR}/sources" "${BACKUP}/sources"
fi
mkdir -p "${TILES_DATA_DIR}"
mv "${TILE_STAGE}" "${TILES_DATA_DIR}/tiles"
mv "${SRC_STAGE}" "${TILES_DATA_DIR}/sources"
success "activated: ${TILES_DATA_DIR}/tiles + ${TILES_DATA_DIR}/sources"
success "previous pyramid preserved at ${BACKUP:+${BACKUP}}"