#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# mapserver-entrypoint.sh — nars-tiles container bootstrap.
#
#   1. Templates the wms_onlineresource into /etc/mapserver/xyz.map from the
#      TILES_PUBLIC_URL env var, so WMS GetCapabilities advertises a reachable
#      endpoint. Idempotent: a mapfile that was already templated has no
#      placeholder left to replace.
#   2. Starts fcgiwrap (spawn-fcgi, foreground) holding the MapServer CGI
#      binary at /run/fcgiwrap/mapserv.socket.
#   3. execs nginx, which serves the XYZ pyramid and proxies /wms to the
#      socket. nginx becomes PID 1; the fcgiwrap child is reaped by the
#      container runtime when it exits.
#
# Socket/ownership model:
#   spawn-fcgi creates the socket as root, chowns it to www-data:www-data
#   (mode 0660), and drops the fcgiwrap process to www-data — the same user the
#   Debian nginx workers run as, so the proxy handshake succeeds on both sides
#   and rendered WMS never needs root.
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

RUN_DIR=/run/fcgiwrap
SOCKET="${RUN_DIR}/mapserv.socket"
MAPFILE_PATH=/etc/mapserver/xyz.map
MAPSERV=/usr/lib/cgi-bin/mapserv
WORKER_USER=www-data

# Keep RUN_DIR root-owned. The image used to pre-chown it to www-data, which is
# fine on a normal build/run but breaks under rootless container runtimes: the
# entrypoint's uid (container root) maps to a different host sub-uid than the
# www-data owner, so creating the pidfile or the socket in that directory hits
# EACCES ("Permission denied"). spawn-fcgi chowns JUST the socket (0660), so
# nginx's www-data workers can still connect without any directory ownership.
mkdir -p "${RUN_DIR}"

if [[ -n "${TILES_PUBLIC_URL:-}" ]]; then
    sed -i "s#__WMS_ONLINERESOURCE__#${TILES_PUBLIC_URL}#g" "${MAPFILE_PATH}"
fi

if [[ ! -x "${MAPSERV}" ]]; then
    echo "✖ ${MAPSERV} missing — is cgi-mapserver installed?" >&2
    exit 1
fi

spawn-fcgi -s "${SOCKET}" -M 0660 -u "${WORKER_USER}" -g "${WORKER_USER}" \
    -F 1 -P "${RUN_DIR}/fcgiwrap.pid" -- /usr/sbin/fcgiwrap

exec nginx -g 'daemon off;'