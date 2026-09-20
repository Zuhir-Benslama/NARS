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

    assets_dir = args.api.parent / "assets"
    if assets_dir.is_dir():
        missing = {name for name in api_assets if not (assets_dir / name).is_file()}
        if missing:
            print(
                "✖ nars-api/wwwroot/index.html references bundle(s) missing on disk: "
                f"{sorted(missing)}",
                file=sys.stderr,
            )
            return 1
    else:
        print(
            "  ↷ api entrypoint has no sibling assets/ dir — "
            "skipping on-disk existence check",
            file=sys.stderr,
        )

    print(
        f"✓ / and /map reference the same {len(api_assets)} asset(s): {sorted(api_assets)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
