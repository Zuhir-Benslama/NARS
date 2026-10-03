"""Assert the nginx-served SPA and the backend-served /map page load the same bundle.

Every frontend deploy produces content-hashed, immutable assets under
``assets/`` (``index-<hash>.js``, ``index-<hash>.css``, ...). The SPA is
reachable through TWO HTML entrypoints that must stay in lockstep:

* ``/``   — served by the nars-vite nginx image from ``index.html``
* ``/map``— served by nars-api (PagesController) from ``nars-api/wwwroot/``

Both copies are built from the same source, so they must reference the same
asset filenames. If they drift — e.g. ``make frontend-update`` rebuilds only
nars-vite while ``nars-api/wwwroot/`` still points at the previous build — the
browser 404s on ``/assets/index-<old-hash>.js`` and the SPA never boots (a
blank page after login). This script stops that class of bug at deploy time.

Usage:
    python3 nars-infra/scripts/check_frontend_bundle_sync.py \
        --frontend nars-web/dist/index.html \
        --api nars-api/wwwroot/index.html

When the entrypoints were copied out of running pods (which is how the deploy
guards check them), they sit in a temp dir with no sibling ``assets/``, so the
on-disk existence check would be skipped. Pass ``--api-assets-listing`` with the
output of ``ls -1 <pod's assets dir>`` to check existence against the files that
are actually deployed instead.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

_ASSET_RE = re.compile(r"assets/([A-Za-z0-9._-]+)")


def collect_assets(path: Path) -> set[str]:
    text = path.read_text(encoding="utf-8")
    return {name for name in _ASSET_RE.findall(text) if name}


def read_listing(path: Path) -> set[str]:
    """Parse a `ls -1` style listing into a set of filenames."""
    text = path.read_text(encoding="utf-8")
    return {line.strip() for line in text.splitlines() if line.strip()}


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check that two index.html entrypoints reference the same bundle assets."
    )
    parser.add_argument(
        "--frontend", required=True, type=Path, help="nginx-served index.html"
    )
    parser.add_argument(
        "--api", required=True, type=Path, help="backend-served (wwwroot) index.html"
    )
    parser.add_argument(
        "--api-assets-listing",
        type=Path,
        default=None,
        help=(
            "file listing the deployed wwwroot assets dir, one filename per line "
            "(e.g. `ls -1 /app/wwwroot/assets` from the running pod). Use when "
            "--api was copied out of a container and has no sibling assets/ dir."
        ),
    )
    args = parser.parse_args()

    if not args.frontend.is_file() or not args.api.is_file():
        print(
            f"✖ missing entrypoint index.html: {args.frontend} / {args.api}",
            file=sys.stderr,
        )
        return 2

    frontend_assets = collect_assets(args.frontend)
    api_assets = collect_assets(args.api)
    if not frontend_assets or not api_assets:
        print(
            "✖ could not find any assets/* bundle references in the entrypoints",
            file=sys.stderr,
        )
        return 2

    if frontend_assets != api_assets:
        print(
            "✖ / and /map reference different bundles:\n"
            f"  /     ({args.frontend}): {sorted(frontend_assets)}\n"
            f"  /map  ({args.api}): {sorted(api_assets)}",
            file=sys.stderr,
        )
        return 1

    # Existence check. Order matters: an explicit listing is what the deploy
    # guards pass (the entrypoint is in a temp dir there, so the sibling-dir
    # fallback below can never fire), and it is the only source that reflects
    # the files actually deployed rather than the repo working tree.
    if args.api_assets_listing is not None:
        if not args.api_assets_listing.is_file():
            print(
                f"✖ missing assets listing: {args.api_assets_listing}",
                file=sys.stderr,
            )
            return 2
        available = read_listing(args.api_assets_listing)
        missing = {name for name in api_assets if name not in available}
        if missing:
            print(
                "✖ /map references bundle(s) absent from the deployed wwwroot "
                f"assets dir: {sorted(missing)}",
                file=sys.stderr,
            )
            return 1
    else:
        assets_dir = args.api.parent / "assets"
        if assets_dir.is_dir():
            missing = {name for name in api_assets if not (assets_dir / name).is_file()}
            if missing:
                print(
                    "✖ nars-api/wwwroot/index.html references bundle(s) missing "
                    f"on disk: {sorted(missing)}",
                    file=sys.stderr,
                )
                return 1
        else:
            # Loud on purpose: a skipped existence check is not a pass. Say so
            # on stdout (where callers show it) and name the flag that closes it.
            print(
                "  ↷ WARNING: existence check did NOT run — no sibling assets/ "
                "dir next to the api entrypoint. Pass --api-assets-listing to "
                "verify the deployed files.",
                file=sys.stderr,
            )
            print(
                "  ↷ existence check skipped (no sibling assets/ dir) — "
                "bundles are equal but their presence on disk is UNVERIFIED",
                file=sys.stdout,
            )

    print(
        f"✓ / and /map reference the same {len(api_assets)} asset(s): {sorted(api_assets)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
