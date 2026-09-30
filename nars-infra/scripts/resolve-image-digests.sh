#!/usr/bin/env bash
# Resolves pushed image tags to their registry manifest digests, so non-dev
# deployments can be pinned by content address instead of a mutable tag.
#
# Usage:
#   resolve-image-digests.sh <org> <tag> <image> [<image> ...]
#
# For each image, reads the manifest digest of <org>/<image>:<tag> from the
# registry (using this docker context's login) and prints:
#   <image> <sha256:...>
# one per line. The digest is that of the OCI index/manifest the tag points at,
# so pinning <org>/<image>@<digest> pulls byte-for-byte the same content, even
# if the tag is later re-targeted.
#
# FAILS (non-zero) if any inspected image is absent or unreadable, so the
# caller can fail closed on a tag that was never pushed or was overwritten.
#
# Test seam: set INSPECT_JSON_FILE to a saved `docker manifest inspect --verbose
# <ref>` payload (a JSON array) to exercise the digest-parsing path offline
# without a registry; make/infra-lint-digest-guard uses this. Set
# INSPECT_INSECURE=1 to pass --insecure for a plain-HTTP internal registry.
set -euo pipefail

if [ "$#" -lt 3 ]; then
  echo "usage: $0 <org> <tag> <image> [<image> ...]" >&2
  exit 2
fi

org="$1"
tag="$2"
shift 2

export DOCKER_CLI_EXPERIMENTAL=enabled

parser='import json,sys
d = json.load(sys.stdin)
e = d[0] if isinstance(d, list) else d
print(e["Descriptor"]["digest"])'

for image in "$@"; do
  ref="${org}/${image}:${tag}"
  if [ -n "${INSPECT_JSON_FILE:-}" ]; then
    digest="$(python3 -c "${parser}" < "${INSPECT_JSON_FILE}")" \
      || { echo "✖ resolve-image-digests: cannot parse ${INSPECT_JSON_FILE} (expected docker manifest inspect --verbose output)" >&2; exit 1; }
  else
    # `docker buildx imagetools inspect --format '{{.Manifest.Digest}}'` returns
    # the TOP-LEVEL digest the tag points at: the OCI index digest for a
    # multi-arch image, or the manifest digest for a single-arch one. (Plain
    # `docker manifest inspect --verbose` instead returns the child platform
    # manifests for an index, whose digest would wrongly pin a single arch.)
    digest="$(docker buildx imagetools inspect --format '{{.Manifest.Digest}}' "${ref}" 2>/dev/null)" \
      || { echo "✖ resolve-image-digests: cannot inspect ${ref} — is it pushed to the registry this docker context is logged into?" >&2; exit 1; }
  fi
  printf '%s %s\n' "${image}" "${digest}"
done