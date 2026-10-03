"""Tests for check_frontend_bundle_sync.py.

The script is the guard against the blank-page-after-login bug: / (nginx image)
and /map (nars-api wwwroot) must reference the same content-hashed bundle, and
those bundles must actually exist. The existence half is the easy one to lose —
the deploy guards copy both index.html files out of running pods into a temp
dir, where the script's sibling-assets-dir fallback cannot fire. These tests
pin the behaviour of --api-assets-listing so that path cannot silently regress
back into "checked nothing, reported success".
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

SCRIPT = Path(__file__).with_name("check_frontend_bundle_sync.py")

ASSETS = ("index-abc123.js", "index-def456.css")


def _index(*names: str) -> str:
    tags = "\n".join(f'<script src="/assets/{n}"></script>' for n in names)
    return f"<!doctype html><html><head>{tags}</head><body></body></html>"


def _run(frontend: Path, api: Path, *extra: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [
            sys.executable,
            str(SCRIPT),
            "--frontend",
            str(frontend),
            "--api",
            str(api),
            *extra,
        ],
        capture_output=True,
        text=True,
        check=False,
    )


@pytest.fixture
def entrypoints(tmp_path: Path) -> tuple[Path, Path]:
    frontend = tmp_path / "frontend.html"
    api = tmp_path / "api.html"
    frontend.write_text(_index(*ASSETS), encoding="utf-8")
    api.write_text(_index(*ASSETS), encoding="utf-8")
    return frontend, api


def _listing(tmp_path: Path, *names: str) -> Path:
    path = tmp_path / "api-assets.txt"
    path.write_text("".join(f"{n}\n" for n in names), encoding="utf-8")
    return path


def test_matching_entrypoints_pass(entrypoints):
    frontend, api = entrypoints
    result = _run(frontend, api)
    assert result.returncode == 0, result.stderr


def test_bundle_mismatch_fails(entrypoints, tmp_path):
    frontend, api = entrypoints
    api.write_text(_index("index-OLD.js"), encoding="utf-8")
    result = _run(
        frontend, api, "--api-assets-listing", str(_listing(tmp_path, "index-OLD.js"))
    )
    assert result.returncode == 1
    assert "different bundles" in result.stderr


def test_listing_catches_asset_absent_from_deployed_wwwroot(entrypoints, tmp_path):
    # The regression this whole flag exists for: equal references, but the
    # bundle is not in the deployed assets dir -> /map 404s -> blank page.
    frontend, api = entrypoints
    result = _run(
        frontend, api, "--api-assets-listing", str(_listing(tmp_path, ASSETS[0]))
    )
    assert result.returncode == 1
    assert "absent from the deployed wwwroot" in result.stderr
    assert ASSETS[1] in result.stderr


def test_listing_with_every_asset_present_passes(entrypoints, tmp_path):
    frontend, api = entrypoints
    result = _run(
        frontend, api, "--api-assets-listing", str(_listing(tmp_path, *ASSETS))
    )
    assert result.returncode == 0, result.stderr


def test_missing_listing_file_is_an_error_not_a_pass(entrypoints, tmp_path):
    frontend, api = entrypoints
    result = _run(frontend, api, "--api-assets-listing", str(tmp_path / "nope.txt"))
    assert result.returncode == 2
    assert "missing assets listing" in result.stderr


def test_skipped_existence_check_is_reported_loudly(tmp_path):
    # No listing and no sibling assets/ dir: equality still holds, but the
    # caller must be told on stdout (which the Makefile guards display) that
    # presence on disk was NOT verified — a silent skip reads as a pass.
    frontend = tmp_path / "frontend.html"
    api = tmp_path / "api.html"
    frontend.write_text(_index(*ASSETS), encoding="utf-8")
    api.write_text(_index(*ASSETS), encoding="utf-8")
    result = _run(frontend, api)
    assert result.returncode == 0, result.stderr
    assert "UNVERIFIED" in result.stdout


def test_sibling_assets_dir_is_used_when_present(tmp_path):
    # The local gate (make infra-lint-frontend-bundle) passes real repo paths,
    # so the sibling-dir path must keep working without a listing.
    wwwroot = tmp_path / "wwwroot"
    (wwwroot / "assets").mkdir(parents=True)
    frontend = tmp_path / "frontend.html"
    frontend.write_text(_index(*ASSETS), encoding="utf-8")
    api = wwwroot / "index.html"
    api.write_text(_index(*ASSETS), encoding="utf-8")
    for name in ASSETS:
        (wwwroot / "assets" / name).write_text("/* bundle */", encoding="utf-8")

    result = _run(frontend, api)
    assert result.returncode == 0, result.stderr
    assert "UNVERIFIED" not in result.stdout

    (wwwroot / "assets" / ASSETS[0]).unlink()
    result = _run(frontend, api)
    assert result.returncode == 1
    assert "missing on disk" in result.stderr


def test_missing_entrypoint_is_an_error(tmp_path):
    missing = tmp_path / "gone.html"
    present = tmp_path / "here.html"
    present.write_text(_index(*ASSETS), encoding="utf-8")
    result = _run(missing, present)
    assert result.returncode == 2
    assert "missing entrypoint" in result.stderr


def test_entrypoint_without_asset_references_is_an_error(tmp_path):
    bare = "<!doctype html><html><body>no bundles here</body></html>"
    frontend = tmp_path / "frontend.html"
    api = tmp_path / "api.html"
    frontend.write_text(bare, encoding="utf-8")
    api.write_text(bare, encoding="utf-8")
    result = _run(frontend, api)
    assert result.returncode == 2
    assert "could not find any assets" in result.stderr
