"""Tests for the png-to-pdf.py admin utility.

The script is a CLI entrypoint (it reads sys.argv and calls sys.exit), so
these tests drive it as a subprocess rather than importing its module-level
side effects. Pillow is only needed to fabricate PNG fixtures.
"""

import subprocess
import sys
from pathlib import Path

from PIL import Image

SCRIPT = Path(__file__).with_name("png-to-pdf.py")


def run(dir_arg: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), dir_arg],
        capture_output=True,
        text=True,
        check=False,
    )


def write_png(path: Path) -> None:
    with Image.new("RGB", (4, 4), color=(200, 30, 30)) as img:
        img.save(path)


def test_help_exits_zero() -> None:
    res = subprocess.run(
        [sys.executable, str(SCRIPT), "-h"], capture_output=True, text=True, check=False
    )
    assert res.returncode == 0
    assert "Usage:" in res.stderr


def test_no_arguments_exits_one() -> None:
    res = subprocess.run(
        [sys.executable, str(SCRIPT)], capture_output=True, text=True, check=False
    )
    assert res.returncode == 1
    assert "Usage:" in res.stderr


def test_missing_directory_exits_one(tmp_path: Path) -> None:
    res = run(str(tmp_path / "does-not-exist"))
    assert res.returncode == 1
    assert "not a directory" in res.stderr


def test_empty_directory_exits_zero(tmp_path: Path) -> None:
    res = run(str(tmp_path))
    assert res.returncode == 0
    assert "No PNG files found" in res.stdout


def test_converts_png_to_pdf(tmp_path: Path) -> None:
    src = tmp_path / "map.png"
    write_png(src)

    res = run(str(tmp_path))

    assert res.returncode == 0
    pdf = tmp_path / "map.pdf"
    assert pdf.exists()
    assert pdf.read_bytes().startswith(b"%PDF")
    assert "Converting map.png -> map.pdf..." in res.stdout


def test_corrupt_png_reports_failure(tmp_path: Path) -> None:
    broken = tmp_path / "broken.png"
    broken.write_bytes(b"this is not a png")

    res = run(str(tmp_path))

    assert res.returncode == 1
    assert "cannot open 'broken.png'" in res.stderr
    assert "1 failure" in res.stderr


def test_mixed_directory_converts_good_and_reports_bad(tmp_path: Path) -> None:
    write_png(tmp_path / "ok.png")
    (tmp_path / "bad.png").write_bytes(b"not an image either")

    res = run(str(tmp_path))

    assert res.returncode == 1
    assert (tmp_path / "ok.pdf").exists()
    assert not (tmp_path / "bad.pdf").exists()
    assert "1 failure" in res.stderr
