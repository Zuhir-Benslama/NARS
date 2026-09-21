#!/usr/bin/env python3
"""Build the roads training set from validated drafts + satellite imagery.

Reads `labels.jsonl` (one JSON object per accepted road draft, exported from
ai_draft_features: id, feature_type, geometry{LineString coords=[lon,lat]},
confidence, commune_id) and produces 1024x1024 training chips:

  data/tiles/{z}/{y}/{x}.jpg  — raw satellite tiles (Esri World Imagery, cached)
  data/chips/{chip_id}.png    — composited 1024px satellite image
  data/chips/{chip_id}_mask.png — road mask (all accepted roads clipped to the
                                  chip), ~8px stroke at 1024px (z18 context)
  data/chips/manifest.csv     — chip_id, seeds (contributing label guids),
                                commune_ids, has_road, bounds

Chips are one row per *unique 4x4 tile-grid anchor* (the serving window is
1024px = 4x4 z18 tiles), so nearby road drafts share a context instead of
producing near-duplicate chips. `--negatives N` adds random empty contexts
inside the labels' extent to balance the background class.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import math
import random
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
import requests
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
TILE_URL = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"
ZOOM = 18
TILE = 256
CHIP_TILES = 4  # 1024px context at z18 == the serving inference window
CHIP = TILE * CHIP_TILES
LAT_LIMIT = 85.05112878
ROAD_WIDTH_PX = 8
FETCH_THREADS = 8
HTTP_HEADERS = {"User-Agent": "Mozilla/5.0 (X11; Linux x86_64) NARS-data-prep/1.0"}


def lat_to_tile_y(lat: float, zoom: int) -> float:
    lat = max(-LAT_LIMIT, min(LAT_LIMIT, lat))
    lat_rad = math.radians(lat)
    merc = math.log(math.tan(lat_rad) + 1 / math.cos(lat_rad))
    return ((1 - merc / math.pi) / 2) * (2**zoom)


def lon_to_tile_x(lon: float, zoom: int) -> float:
    return ((lon + 180) / 360) * (2**zoom)


def tile_bounds(z: int, x: int, y: int) -> tuple[float, float, float, float]:
    """(west, south, east, north) of one slippy tile."""
    n = 2**z
    w = x / n * 360 - 180
    e = (x + 1) / n * 360 - 180
    n_ = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * y / n))))
    s = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * (y + 1) / n))))
    return w, s, e, n_


def context_bounds(x0: int, y0: int) -> tuple[float, float, float, float]:
    """West/south/east/north of the CHIP_TILES x CHIP_TILES context at (x0, y0)."""
    w, _, _, _ = tile_bounds(ZOOM, x0, y0)
    _, _, e, _ = tile_bounds(ZOOM, x0 + CHIP_TILES - 1, y0)
    _, s, _, _ = tile_bounds(ZOOM, x0, y0 + CHIP_TILES - 1)
    _, _, _, n = tile_bounds(ZOOM, x0, y0)
    return w, s, e, n


class TileCache:
    def __init__(self, root: Path) -> None:
        self.root = root

    def path(self, z: int, x: int, y: int) -> Path:
        return self.root / str(z) / str(y) / f"{x}.jpg"

    def fetch(self, z: int, x: int, y: int, session: requests.Session) -> bytes:
        p = self.path(z, x, y)
        if p.exists() and p.stat().st_size > 0:
            return p.read_bytes()
        for attempt in range(4):
            try:
                r = session.get(
                    TILE_URL.format(z=z, y=y, x=x),
                    headers=HTTP_HEADERS,
                    timeout=30,
                )
                r.raise_for_status()
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(r.content)
                return r.content
            except requests.RequestException as exc:
                if attempt == 3:
                    raise
                import time

                time.sleep(0.5 * (attempt + 1))
        raise RuntimeError("unreachable")


def chip_anchor(lon: float, lat: float) -> tuple[int, int]:
    """4x4-grid anchor (x0, y0) whose fl context contains lon/lat."""
    tx = math.floor(lon_to_tile_x(lon, ZOOM))
    ty = math.floor(lat_to_tile_y(lat, ZOOM))
    x0 = max(0, min(tx - 1, 2**ZOOM - CHIP_TILES))
    y0 = max(0, min(ty - 1, 2**ZOOM - CHIP_TILES))
    return x0, y0


def load_lines(path: Path) -> list[dict]:
    lines = []
    for raw in path.read_text().splitlines():
        if not raw.strip():
            continue
        rec = json.loads(raw)
        geom = rec["geometry"]
        if geom.get("type") != "LineString":
            print(f"skip non-LineString {rec['id']} ({geom.get('type')})")
            continue
        lines.append(
            {
                "id": rec["id"],
                "commune_id": rec["commune_id"],
                "confidence": rec.get("confidence", 0.0),
                "line": [(float(a), float(b)) for a, b in geom["coordinates"]],
            }
        )
    return lines


def render_mask(all_lines: list[dict], x0: int, y0: int) -> np.ndarray:
    """Rasterize every accepted line inside the context onto a road mask."""
    w, s, e, n = context_bounds(x0, y0)
    supersample = 2
    canvas = CHIP * supersample
    img = Image.new("L", (canvas, canvas), 0)
    draw = ImageDraw.Draw(img)
    for line in all_lines:
        pts_px: list[tuple[float, float]] = []
        for lon, lat in line["line"]:
            if w <= lon <= e and s <= lat <= n:
                tx = lon_to_tile_x(lon, ZOOM) - x0
                ty = lat_to_tile_y(lat, ZOOM) - y0
                pts_px.append((tx * TILE * supersample, ty * TILE * supersample))
        if len(pts_px) < 2:
            continue
        draw.line(pts_px, fill=255, width=ROAD_WIDTH_PX * supersample, joint="curve")
    return np.array(img.resize((CHIP, CHIP), Image.LANCZOS))  # type: ignore[arg-type]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--labels", default=str(ROOT / "data" / "labels.jsonl"))
    ap.add_argument("--out", default=str(ROOT / "data"))
    ap.add_argument("--negatives", type=int, default=0, help="random empty contexts")
    ap.add_argument(
        "--limit", type=int, default=0,
        help="only build the first N anchor contexts (smoke test)",
    )
    args = ap.parse_args()

    out = Path(args.out)
    tiles = TileCache(out / "tiles")
    chips_dir = out / "chips"
    chips_dir.mkdir(parents=True, exist_ok=True)

    all_lines = load_lines(Path(args.labels))
    print(f"loaded {len(all_lines)} accepted road lines")

    anchors: dict[tuple[int, int], list[int]] = {}
    for idx, rec in enumerate(all_lines):
        xs = [p[0] for p in rec["line"]]
        ys = [p[1] for p in rec["line"]]
        a = chip_anchor(sum(xs) / len(xs), sum(ys) / len(ys))
        anchors.setdefault(a, []).append(idx)
    print(f"{len(anchors)} unique 4x4 contexts seeded by {len(all_lines)} lines")

    if args.negatives:
        lon0 = min(p[0] for rec in all_lines for p in rec["line"])
        lon1 = max(p[0] for rec in all_lines for p in rec["line"])
        lat0 = min(p[1] for rec in all_lines for p in rec["line"])
        lat1 = max(p[1] for rec in all_lines for p in rec["line"])
        added = 0
        while added < args.negatives:
            rng = random.Random(f"neg-{added}")
            a = chip_anchor(rng.uniform(lon0, lon1), rng.uniform(lat0, lat1))
            if a not in anchors:
                anchors[a] = []
                added += 1
        print(f"after negatives: {len(anchors)} contexts")

    session = requests.Session()

    def build_chip(key: tuple[int, int], seed_idx: list[int]) -> None:
        x0, y0 = key
        image = Image.new("RGB", (CHIP, CHIP), (0, 0, 0))
        for dx in range(CHIP_TILES):
            for dy in range(CHIP_TILES):
                try:
                    im = Image.open(io.BytesIO(tiles.fetch(ZOOM, x0 + dx, y0 + dy, session)))
                    im = im.convert("RGB")
                except Exception:
                    im = Image.new("RGB", (TILE, TILE), (0, 0, 0))
                image.paste(im, (dx * TILE, dy * TILE))
        mask = render_mask(all_lines, x0, y0)
        has_road = bool((mask > 127).any())
        if not seed_idx and not has_road:
            return
        chip_id = f"z{ZOOM}_x{x0}_y{y0}"
        image.save(chips_dir / f"{chip_id}.png")
        Image.fromarray(mask).save(chips_dir / f"{chip_id}_mask.png")
        w, s, e, n = context_bounds(x0, y0)
        commune_ids = sorted({all_lines[i]["commune_id"] for i in seed_idx})
        return {
            "chip_id": chip_id,
            "seeds": ",".join(all_lines[i]["id"] for i in seed_idx[:96]),
            "commune_ids": ",".join(str(c) for c in commune_ids),
            "has_road": 1 if has_road else 0,
            "bounds": f"{w},{s},{e},{n}",
        }

    manifest_rows = []
    errors = 0
    anchors_items = list(anchors.items())
    if args.limit:
        anchors_items = anchors_items[: args.limit]
        print(f"smoke test: {len(anchors_items)} contexts")
    with ThreadPoolExecutor(max_workers=FETCH_THREADS) as ex:
        futures = {ex.submit(build_chip, k, v): k for k, v in anchors_items}
        for i, fut in enumerate(futures):
            try:
                row = fut.result()
                if row:
                    manifest_rows.append(row)
            except Exception as exc:
                errors += 1
                print(f"context {futures[fut]}: {exc}")
            if (i + 1) % 50 == 0:
                print(f"{i + 1}/{len(anchors)} contexts ...")

    with open(chips_dir / "manifest.csv", "w", newline="") as fh:
        w_csv = csv.DictWriter(
            fh, fieldnames=["chip_id", "seeds", "commune_ids", "has_road", "bounds"]
        )
        w_csv.writeheader()
        for row in manifest_rows:
            w_csv.writerow(row)
    pos = sum(1 for r in manifest_rows if r["has_road"])
    print(f"done: {len(manifest_rows)} chips ({pos} with roads, {errors} errors)")
    print(f"written to {chips_dir}")


if __name__ == "__main__":
    main()