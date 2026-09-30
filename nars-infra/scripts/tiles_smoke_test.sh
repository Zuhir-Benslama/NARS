#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# tiles_smoke_test.sh — end-to-end verification of the nars-tiles container.
#
# No cluster, no registry: this runs on any machine with docker + curl. It
#   1. builds the nars-tiles image,
#   2. synthesizes a tiny world pyramid with tiles_synth.py inside the pinned
#      GDAL image,
#   3. boots the tile server with it mounted at /data,
#   4. asserts the full contract: static XYZ (200 + CORS + 256px), deleted tile
#      (real 404), WMS GetCapabilities/GetMap, and the literal-'+' TILE= syntax
#      (with %2B locked to FAIL, per MapServer's plustospace quirk).
#
# Requires: docker, curl. Env overrides:
#   TILES_GDAL_IMAGE   GDAL image for pyramid synthesis
#                       (default: ghcr.io/osgeo/gdal:ubuntu-small-3.10.3)
#   TILES_SMOKE_PORT   host port for the test server (default: an ephemeral
#                      high port chosen at runtime, so parallel smoke tests and
#                      leftover rootless-docker bindings never collide)
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

TILES_GDAL_IMAGE="${TILES_GDAL_IMAGE:-ghcr.io/osgeo/gdal:ubuntu-small-3.10.3}"
SCRIPT_DIR="$(cd -P "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -P "${SCRIPT_DIR}/../.." && pwd)"
TILE_IMAGE="nars-tiles:smoke-test"

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; RESET='\033[0m'
info()    { echo -e "${CYAN}[INFO]${RESET} $*"; }
success() { echo -e "${GREEN}[OK]${RESET}   $*"; }
die()     { echo -e "${RED}[ERROR]${RESET} $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || die "docker not found"
command -v curl >/dev/null 2>&1 || die "curl not found"

CID="nars-tiles-smoke-$$"
TMP="$(mktemp -d)"
cleanup() {
    docker rm -f "${CID}" >/dev/null 2>&1 || true
    rm -rf "${TMP}"
}
trap cleanup EXIT INT TERM

# Allocate an ephemeral high port (python3 for cross-host determinism; a tiny
# bind/release race is acceptable for a smoke test and avoids the rootless-docker
# port-lingering failures seen when reusing a fixed port).
PORT="${TILES_SMOKE_PORT:-$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')}"
BASE="http://127.0.0.1:${PORT}"
export PORT BASE

status_of() { curl -s -o /dev/null -w '%{http_code}' "$1"; }
expect_status() { # expect_status <expected> <url>
    local expected="$1" url="$2" got
    got="$(status_of "${url}")"
    if [ "${got}" != "${expected}" ]; then
        die "expected HTTP ${expected} for ${url}, got ${got}"
    fi
    success "HTTP ${expected}: ${url}"
}

# ── 1. Build the tile server image ─────────────────────────────────────────
info "Building ${TILE_IMAGE} (context: repo root)..."
docker build -q -f "${REPO_ROOT}/nars-infra/docker/Dockerfile.nars-tiles" \
    -t "${TILE_IMAGE}" "${REPO_ROOT}" >/dev/null
success "Image built"

# ── 2. Synthesize the tiny pyramid ─────────────────────────────────────────
info "Synthesizing pyramid (tiles_synth.py inside ${TILES_GDAL_IMAGE})..."
docker run --rm \
    -v "${REPO_ROOT}:/repo:ro" \
    -v "${TMP}:/data" \
    "${TILES_GDAL_IMAGE}" \
    /usr/bin/python3 /repo/nars-infra/scripts/tiles_synth.py /data
DELETED_TILE="$(cat "${TMP}/deleted_tile.txt")"
success "Pyramid ready (deleted leaf tile: ${DELETED_TILE})"

# ── 3. Boot the tile server ────────────────────────────────────────────────
info "Starting tile server on 127.0.0.1:${PORT}..."
docker run -d --rm --name "${CID}" \
    -v "${TMP}:/data" \
    -e "TILES_PUBLIC_URL=${BASE}/wms" \
    -p "127.0.0.1:${PORT}:80" \
    "${TILE_IMAGE}" >/dev/null

# WMS GetCapabilities doubles as readiness: reaching it requires nginx AND the
# fcgiwrap socket AND a mapfile whose tileindex loads cleanly.
cap="${TMP}/capabilities.xml"
ready_code=000
for _ in $(seq 1 60); do
    # `|| echo 000` keeps the assignment honest under set -e: a refused/empty
    # reply (curl exit 7/52) must not abort the wait loop.
    ready_code="$(status_of "${BASE}/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetCapabilities" || echo 000)"
    [ "${ready_code}" = "200" ] && break
    sleep 1
done
[ "${ready_code}" = "200" ] || die "tile server did not become ready (last HTTP ${ready_code})"
curl -s "${BASE}/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetCapabilities" > "${cap}"
success "Server ready; GetCapabilities saved"

# ── 4. Static XYZ assertions ───────────────────────────────────────────────
# 2/0/0.png always exists (the z2 world is fully covered by the synth).
tile_png="${TMP}/fetched_tile.png"
curl -s -D "${TMP}/tile_headers.txt" -H 'Origin: https://map.nars.dz' \
    -o "${tile_png}" "${BASE}/tiles/2/0/0.png"
grep -qi '^Content-Type: image/png' "${TMP}/tile_headers.txt" \
    || die "tile Content-Type is not image/png"
grep -qi '^Access-Control-Allow-Origin: https://map.nars.dz' "${TMP}/tile_headers.txt" \
    || die "tile did not reflect the Origin CORS header"
dims="$(docker run --rm -v "${TMP}:/inspect" "${TILES_GDAL_IMAGE}" \
    gdalinfo /inspect/fetched_tile.png | sed -n 's/^Size is //p' | head -1 | tr -d ' ')"
[ "${dims}" = "256,256" ] || die "tile dimensions are ${dims}, expected 256x256"
success "Static tile: 256x256 PNG + CORS"

expect_status 404 "${BASE}/tiles/${DELETED_TILE}"

# ── 5. WMS assertions ───────────────────────────────────────────────────────
# GetCapabilities advertises the layer and the templated onlineresource.
grep -q '<Name>xyz</Name>' "${cap}" || die "GetCapabilities does not advertise layer 'xyz'"
grep -q "${BASE}/wms" "${cap}" || die "GetCapabilities does not advertise the templated onlineresource"
success "GetCapabilities advertises layer 'xyz' @ ${BASE}/wms"

# GetMap for the whole world in EPSG:3857 must re-render from the source tif.
wms="${TMP}/wms.png"
curl -s -o "${wms}" \
    "${BASE}/wms?SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1&LAYERS=xyz&STYLES=&FORMAT=image/png&SRS=EPSG:3857&BBOX=-20037508.342789244,-20037508.342789244,20037508.342789244,20037508.342789244&WIDTH=256&HEIGHT=256"
[ "$(file --brief --mime-type "${wms}" 2>/dev/null || echo unknown)" = "image/png" ] \
    || die "GetMap did not return a PNG"
dims="$(docker run --rm -v "${TMP}:/inspect" "${TILES_GDAL_IMAGE}" \
    gdalinfo /inspect/wms.png | sed -n 's/^Size is //p' | head -1 | tr -d ' ')"
[ "${dims}" = "256,256" ] || die "GetMap dimensions are ${dims}, expected 256x256"
success "WMS GetMap: 256x256 PNG"

# mode=tile: literal '+',''-separated TILE coordinates (the MapLibre WMS
# contract). Earlier belief held that %2B must be rejected; in cgi-mapserver
# 8.4 BOTH forms render (plustospace turns the literal '+' into a space that
# TILE-mode also splits on). Lock the observed contract: both must be 200.
expect_status 200 \
    "${BASE}/wms?SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1&LAYERS=xyz&STYLES=&FORMAT=image/png&TILEMODE=gmap&TILE=1+1+2"
expect_status 200 \
    "${BASE}/wms?SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1&LAYERS=xyz&STYLES=&FORMAT=image/png&TILEMODE=gmap&TILE=1%2B1%2B2"
success "mode=tile: literal '+' and %2B both render"

echo ""
echo -e "${GREEN}✓ All tile-server smoke assertions passed${RESET}"