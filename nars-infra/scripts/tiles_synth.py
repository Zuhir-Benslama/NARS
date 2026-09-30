#!/usr/bin/env python3
"""tiles_synth.py — synthesize a tiny EPSG:3857 pyramid for the tile-server smoke test.

Runs inside the pinned GDAL image (ghcr.io/osgeo/gdal) which ships the osgeo
python bindings, gdal2tiles.py and gdaltindex. Produces the exact layout the
nars-tiles container serves from its /data volume:

    <root>/sources/world_3857.tif    a 4096x4096 world-covering GeoTIFF
    <root>/sources/tileindex.gpkg    gdaltindex footprint index (one feature)
    <root>/tiles/                    gdal2tiles --xyz -z 0-3 pyramid
    <root>/deleted_tile.txt          relative path of one leaf tile that was
                                     deliberately removed from <root>/tiles so
                                     the smoke test can prove missing tiles
                                     return a real 404

The source carries an asymmetric landmark (red block + green band) so a
viewport/ordering mistake in any consumer shows up as obviously wrong output,
and a single-feature tileindex exercises the exact msTryBuildPath3 absolute-path
code path the real urban pyramid relies on.

Example:
    docker run --rm -v "$PWD:/work" ghcr.io/osgeo/gdal:ubuntu-small-3.10.3 \
        python3 /work/nars-infra/scripts/tiles_synth.py /data
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

import numpy as np
from osgeo import gdal, osr

SIZE = 4096
WORLD = 40075016.68557849  # EPSG:3857 world width/height in meters
GDAL2TILES = "gdal2tiles.py"
GDALTINDEX = "gdaltindex"


def _world_tif(path: Path) -> Path:
    """Write a 4096x4096 EPSG:3857 RGB GeoTIFF with a gradient + landmark."""
    srs = osr.SpatialReference()
    srs.ImportFromEPSG(3857)

    driver = gdal.GetDriverByName("GTiff")
    ds = driver.Create(str(path), SIZE, SIZE, 3, gdal.GDT_Byte)
    cell = WORLD / SIZE
    ds.SetGeoTransform((-WORLD / 2.0, cell, 0.0, WORLD / 2.0, 0.0, -cell))
    ds.SetProjection(srs.ExportToWkt())

    x = np.linspace(0.0, 1.0, SIZE, dtype=np.float32).reshape(1, SIZE)
    y = np.linspace(0.0, 1.0, SIZE, dtype=np.float32).reshape(SIZE, 1)
    base = np.clip(128.0 + 110.0 * ((x + y) / 2.0), 0.0, 255.0).astype(np.uint8)

    # Asymmetric landmark: red block top-right quadrant, full-width green band
    # a quarter from the top. Neither feature is centered, so any tile
    # ordering/orientation mistake renders visibly wrong output.
    red = np.zeros((SIZE, SIZE), dtype=np.uint8)
    red[500:900, 3000:3550] = 255
    green = np.zeros((SIZE, SIZE), dtype=np.uint8)
    green[750:820, :] = 255

    ds.GetRasterBand(1).WriteArray(base)
    ds.GetRasterBand(2).WriteArray(
        np.clip(base.astype(np.int16) + red, 0, 255).astype(np.uint8)
    )
    ds.GetRasterBand(3).WriteArray(
        np.clip(base.astype(np.int16) + green, 0, 255).astype(np.uint8)
    )
    ds.FlushCache()
    ds = None
    return path


def _pyramid(src: Path, tiles_dir: Path) -> None:
    """gdal2tiles --xyz -z 0-3 over the source raster."""
    subprocess.run(
        [
            GDAL2TILES,
            "-p",
            "mercator",
            "-r",
            "near",
            "-z",
            "0-3",
            "--xyz",
            "--processes",
            "2",
            str(src),
            str(tiles_dir),
        ],
        check=True,
    )


def _tileindex(src: Path, index_path: Path) -> None:
    """Single-footprint gdaltindex; location field stores the absolute path."""
    subprocess.run(
        [
            GDALTINDEX,
            "-of",
            "GPKG",
            "-t_srs",
            "EPSG:3857",
            "-tileindex",
            "location",
            "-write_absolute_path",
            str(index_path),
            str(src),
        ],
        check=True,
    )


def _delete_leaf_tile(tiles_dir: Path, marker: Path) -> None:
    """Remove one z3 tile and record its relative path for the smoke test."""
    zoom_dir = tiles_dir / "3"
    if not zoom_dir.is_dir():
        raise SystemExit(f"✖ {zoom_dir} missing — gdal2tiles did not produce z3")
    first_x = min(p for p in zoom_dir.iterdir() if p.is_dir())
    first_y = min(p for p in first_x.iterdir() if p.suffix == ".png")
    first_y.unlink()
    marker.write_text(f"{first_y.relative_to(tiles_dir)}\n", encoding="utf-8")
    print(f"  deleted tile: {first_y.relative_to(tiles_dir)}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "root",
        help="output root (mount point); creates sources/, tiles/, deleted_tile.txt",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    sources = root / "sources"
    tiles_dir = root / "tiles"

    # Idempotent: refuse to clobber an existing pyramid (data safety).
    if (sources / "tileindex.gpkg").exists() or tiles_dir.exists():
        raise SystemExit(
            f"✖ {root} already contains tile data — remove it yourself before re-synth"
        )

    # The tile server's nginx worker is NOT root, but host/CI mounts often land
    # as 0700 (mktemp -d). We run as root here, so make the tree traversable and
    # world-readable before handing it to the server. a+rX never adds write.
    print("→ normalizing permissions ...")
    subprocess.run(["chmod", "-R", "a+rX", str(root)], check=True)

    sources.mkdir(parents=True, exist_ok=True)

    print("→ writing world_3857.tif ...")
    src = _world_tif(sources / "world_3857.tif")
    print("→ gdal2tiles --xyz -z 0-3 ...")
    _pyramid(src, tiles_dir)
    print("→ gdaltindex ...")
    _tileindex(src, sources / "tileindex.gpkg")
    print("→ deleting one leaf tile ...")
    _delete_leaf_tile(tiles_dir, root / "deleted_tile.txt")
    print(f"✓ synthesized pyramid at {tiles_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
