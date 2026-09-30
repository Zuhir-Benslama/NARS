#!/usr/bin/env python3
"""check_tile_coverage.py — assert every commune's urban cells are tiled.

Runs offline (inside the GDAL image or on the tile server) after a pyramid
build, against the new staging tree — never against the live /data. Verifies
that each commune's urban cells at a target zoom are covered by at least one
tileindex footprint, and exits non-zero listing the gap communes. This is the
"keep the previous pyramid until the new one passes" gate:

    build_imagery_pyramid.sh   (builds into a staging dir)
    check_tile_coverage.py ... (pass/fail the staging tree)
    only on pass ->              activate the new tree

Cell semantics: a z18 cell is "covered" when the tileindex layer has any
feature whose footprint intersects the cell. The tileindex is derived with
gdaltindex -t_srs EPSG:3857, so footprint geometry and commune geometry must
meet in EPSG:3857 (the script reprojects commune geometry on the fly and
rejects a mismatched tileindex rather than silently reporting 0 coverage).

Example:
    python3 check_tile_coverage.py \
        --tileindex /staging/sources/tileindex.gpkg \
        --areas   /data/areas.gpkg \
        --layer   communes \
        --zoom    18
"""

from __future__ import annotations

import argparse
import math
import sys

import numpy as np
from osgeo import gdal, ogr, osr

WORLD = 40075016.68557849  # EPSG:3857 world width/height in meters
HALF = WORLD / 2.0

TARGET_EPSG = 3857
EXAMPLE_LIMIT = 5  # uncovered tile coordinates to print per commune


def _ensure_3857(
    layer: ogr.Layer, kind: str, path: str
) -> ogr.CoordinateTransformation:
    srs = layer.GetSpatialRef()
    code = srs.GetAuthorityCode(None) if srs else None
    if code == str(TARGET_EPSG):
        return None
    if code is None or srs.GetAuthorityName(None) != "EPSG":
        raise SystemExit(
            f"✖ {kind} {path} has no EPSG authority — refuse to guess reprojection"
        )
    target = osr.SpatialReference()
    target.ImportFromEPSG(TARGET_EPSG)
    return osr.CoordinateTransformation(srs, target)


def _open_source(
    path: str, description: str, require_layer: str | None = None
) -> ogr.DataSource:
    ds = gdal.OpenEx(path, gdal.OF_VECTOR)
    if ds is None:
        raise SystemExit(f"✖ cannot open {description}: {path}")
    if require_layer is not None and ds.GetLayerByName(require_layer) is None:
        names = [ds.GetLayerByIndex(i).GetName() for i in range(ds.GetLayerCount())]
        raise SystemExit(
            f"✖ {description} {path} has no layer '{require_layer}' (layers: {names})"
        )
    return ds


def _tile_range(env, cell: float, zoom: int) -> tuple[int, int, int, int]:
    """Stable z-tile x/y index range for an envelope, clamped to the world grid."""
    max_idx = (1 << zoom) - 1
    x0 = max(0, math.floor((env[0] + HALF) / cell))
    x1 = min(max_idx, math.floor((env[1] + HALF) / cell))
    y0 = max(0, math.floor((HALF - env[3]) / cell))
    y1 = min(max_idx, math.floor((HALF - env[2]) / cell))
    return x0, x1, y0, y1


def _uncovered_cells(
    tiles: ogr.Layer,
    communes: ogr.Layer,
    commune_field: str,
    zoom: int,
    max_cells: int,
) -> list[tuple[str, int, list[tuple[int, int]]]]:
    cell = WORLD / float(1 << zoom)
    target = osr.SpatialReference()
    target.ImportFromEPSG(TARGET_EPSG)

    uncovered: list[tuple[str, int, list[tuple[int, int]]]] = []
    total_communes = 0
    covered_communes = 0

    first = True
    for feature in communes:
        geom = feature.GetGeometryRef()
        if geom is None:
            continue
        field_idx = feature.GetFieldIndex(commune_field)
        name = feature.GetField(field_idx) if field_idx >= 0 else None
        name = str(name) if name is not None else f"fid-{feature.GetFID()}"

        if first:
            # Transform happens once per layer when its SRS differs from 3857.
            ct = _ensure_3857(communes, "areas layer", "areas")
            first = False
        if ct is not None:
            geom.Transform(ct)

        (x0, x1, y0, y1) = _tile_range(geom.GetEnvelope(), cell, zoom)
        width = x1 - x0 + 1
        height = y1 - y0 + 1
        if width * height > max_cells:
            raise SystemExit(
                f"✖ {name}: {width}x{height} cells exceeds --max-cells {max_cells}; "
                "this is a bug guard, raise it deliberately for a pathological commune"
            )

        total_communes += 1

        # Rasterize the commune polygon onto the z-grid inside its envelope, so
        # only cells that ACTUALLY touch the polygon are checked (bbox cells
        # overlapping rural neighbours would otherwise report false gaps).
        driver = gdal.GetDriverByName("MEM")
        ds = driver.Create("", width, height, 1, gdal.GDT_Byte)
        ds.SetProjection(target.ExportToWkt())
        ds.SetGeoTransform(
            (x0 * cell - HALF, cell, 0.0, (y1 + 1) * cell - HALF, 0.0, -cell)
        )
        band = ds.GetRasterBand(1)
        band.Fill(0)
        gdal.RasterizeLayer(band, [geom.Clone()], burn_values=[1])
        mask = band.ReadAsArray()
        ds = None

        bad: list[tuple[int, int]] = []
        for row, col in zip(*np.where(mask > 0), strict=True):
            tile_x = x0 + col
            tile_y = y1 - row
            minx = tile_x * cell - HALF
            maxx = minx + cell
            maxy = HALF - tile_y * cell
            miny = maxy - cell
            tiles.SetSpatialFilterRect(minx, miny, maxx, maxy)
            if tiles.GetFeatureCount() == 0 and len(bad) < EXAMPLE_LIMIT:
                bad.append((tile_x, tile_y))
        if bad:
            uncovered.append((str(name), len(bad), bad))
        else:
            covered_communes += 1

    communes.ResetReading()
    tiles.ResetReading()

    sys.stderr.write(
        f"check_tile_coverage: {covered_communes}/{total_communes} communes fully covered "
        f"at z{zoom}\n"
    )
    return uncovered


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--tileindex", required=True, help="gdaltindex GPKG for the new pyramid"
    )
    ap.add_argument("--areas", required=True, help="GPKG of commune urban polygons")
    ap.add_argument("--layer", default="communes", help="area layer name in --areas")
    ap.add_argument(
        "--commune-field",
        default="name",
        help="commune identifier field (falls back to FID when absent)",
    )
    ap.add_argument("--zoom", type=int, default=18, help="zoom level checked")
    ap.add_argument(
        "--max-cells",
        type=int,
        default=4_000_000,
        help="per-commune cell budget guard (see error text)",
    )
    args = ap.parse_args()

    tiles_ds = _open_source(args.tileindex, "tileindex")
    tiles = tiles_ds.GetLayer(0)
    ct_tiles = _ensure_3857(tiles, "tileindex", args.tileindex)
    if ct_tiles is not None:
        raise SystemExit(
            "✖ tileindex must already be EPSG:3857 (build it with "
            "`gdaltindex -t_srs EPSG:3857`) — a live reproject here would silently "
            "turn every spatial filter into a miss"
        )

    areas_ds = _open_source(args.areas, "areas GPKG", require_layer=args.layer)
    communes = areas_ds.GetLayerByName(args.layer)

    gaps = _uncovered_cells(
        tiles, communes, args.commune_field, args.zoom, args.max_cells
    )

    if not gaps:
        sys.stderr.write(f"✓ no tile coverage gaps at z{args.zoom}\n")
        return 0

    print(f"✖ {len(gaps)} commune(s) have uncovered z{args.zoom} cells:")
    for name, count, examples in sorted(gaps, key=lambda g: -g[1]):
        coords = " ".join(f"{x}/{y}" for x, y in examples)
        print(f"  {name}: {count} uncovered cells (e.g. z{args.zoom}/{coords})")
    return 1


if __name__ == "__main__":
    sys.exit(main())
