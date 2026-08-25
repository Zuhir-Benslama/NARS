"""
Mask -> vector conversion.

Roads:     threshold -> clean -> skeletonize -> graph -> simplified LineStrings
Buildings: threshold -> clean -> connected components -> simplified Polygons
"""

from __future__ import annotations

import logging

import numpy as np
import rasterio
from shapely.errors import GEOSException
from shapely.geometry import LineString, Polygon, mapping

from app.schemas import Feature

__all__ = ["mask_to_linestrings", "mask_to_polygons"]

logger = logging.getLogger("nars-roads.postprocess")

# Simplification tolerance in degrees. ~0.00002 deg is roughly 2m at the
# equator - tune per your imagery resolution.
SIMPLIFY_TOLERANCE = 0.00002
MIN_ROAD_COMPONENT_PX = 40
MIN_BUILDING_COMPONENT_PX = 20


def mask_to_linestrings(
    prob_mask: np.ndarray, transform: rasterio.Affine, threshold: float = 0.5
) -> list[Feature]:
    import sknw
    from skimage.morphology import remove_small_objects, skeletonize

    binary = prob_mask > threshold
    binary = remove_small_objects(binary, max_size=MIN_ROAD_COMPONENT_PX - 1)
    if not binary.any():
        return []

    skeleton = skeletonize(binary)
    graph = sknw.build_sknw(skeleton, multi=True)

    features = []
    for _, _, edge_data in graph.edges(data=True):
        pts = edge_data.get("pts")
        if pts is None or len(pts) < 2:
            continue

        # sknw may hand back float point coordinates; mask indexing below
        # requires ints, so coerce before using pts anywhere.
        pts = np.asarray(pts, dtype=np.intp)

        try:
            coords = [
                rasterio.transform.xy(transform, float(r), float(c)) for r, c in pts
            ]
            line = LineString(coords)
            if line.length == 0:
                continue
            line = line.simplify(SIMPLIFY_TOLERANCE, preserve_topology=False)
            if not line.is_valid or line.geom_type != "LineString":
                continue
            geometry = mapping(line)
            confidence = float(prob_mask[pts[:, 0], pts[:, 1]].mean())
        except (GEOSException, ValueError, TypeError):
            logger.debug("Skipping degenerate road edge", exc_info=True)
            continue
        features.append(
            Feature(
                geometry=geometry,
                properties={
                    "confidence": round(confidence, 4),
                    "feature_type": "road",
                },
            )
        )
    return features


def mask_to_polygons(
    prob_mask: np.ndarray, transform: rasterio.Affine, threshold: float = 0.5
) -> list[Feature]:
    from scipy import ndimage
    from skimage.measure import find_contours, label
    from skimage.morphology import closing, remove_small_objects

    binary = prob_mask > threshold
    binary = closing(binary)
    binary = remove_small_objects(binary, max_size=MIN_BUILDING_COMPONENT_PX - 1)
    if not binary.any():
        return []

    labeled = label(binary)

    features = []
    # Iterate regions by bounding-box slice instead of scanning the full
    # mask per label (`labeled == region_id` is O(regions x H x W) and a
    # dense multi-megapixel tile can produce thousands of labels). Contour
    # and confidence work then happen on the bbox-sized crop only; crop-local
    # coordinates are shifted back into full-mask space below.
    for region_id, (rows, cols) in enumerate(ndimage.find_objects(labeled), start=1):
        if rows is None or cols is None:
            continue
        region_mask = labeled[rows, cols] == region_id

        # Pad with background before contouring: a region that fills its
        # entire bounding box (a plain solid building footprint) makes its
        # crop uniformly True, which has no 0.5-level crossing at all. The
        # ring reproduces the surrounding-zero context the full-mask scan
        # used to provide; coordinates shift back by the same pixel.
        padded = np.pad(region_mask.astype(np.float32), 1)
        contours = find_contours(padded, 0.5)
        if not contours:
            continue

        # Pick the longest contour: `find_contours` returns iso-level
        # crossings and for a mostly-solid binary mask the longest ring
        # almost always traces the true outer boundary while shorter
        # contours are either sub-0.5 internal noise or incomplete rings
        # from non-convex shapes that `closing` filled.
        contour = max(contours, key=len)
        coords = [
            rasterio.transform.xy(
                transform,
                float(r) + rows.start - 1,
                float(c) + cols.start - 1,
            )
            for r, c in contour
        ]
        if len(coords) < 4:
            continue

        try:
            poly = Polygon(coords).simplify(SIMPLIFY_TOLERANCE, preserve_topology=True)
        except (GEOSException, ValueError, TypeError):
            logger.debug("Skipping malformed building polygon", exc_info=True)
            continue

        if not poly.is_valid or poly.area == 0 or poly.geom_type != "Polygon":
            continue

        confidence = float(prob_mask[rows, cols][region_mask].mean())
        features.append(
            Feature(
                geometry=mapping(poly),
                properties={
                    "confidence": round(confidence, 4),
                    "feature_type": "building",
                },
            )
        )
    return features
