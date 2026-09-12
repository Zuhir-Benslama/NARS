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

logger = logging.getLogger("nars-segma.postprocess")

# Simplification tolerance in degrees. ~0.00002 deg is roughly 2m at the
# equator - tune per your imagery resolution.
SIMPLIFY_TOLERANCE = 0.00002
MIN_ROAD_COMPONENT_PX = 40
MIN_BUILDING_COMPONENT_PX = 20


def _haversine_m(points: list[tuple[float, float]]) -> float:
    """Great-circle length of a longitude/latitude polyline in metres."""
    import math

    total = 0.0
    for (lon1, lat1), (lon2, lat2) in zip(points, points[1:]):
        dphi = math.radians(lat2 - lat1)
        dlambda = math.radians(lon2 - lon1)
        a = (
            math.sin(dphi / 2) ** 2
            + math.cos(math.radians(lat1))
            * math.cos(math.radians(lat2))
            * math.sin(dlambda / 2) ** 2
        )
        total += 2 * 6_371_000.0 * math.asin(math.sqrt(a))
    return total


def _apply_rule_cap(
    candidates: list[tuple[float, dict]],
    max_features: int | None,
) -> list[tuple[float, dict]]:
    """Limit the number of emitted features, keeping the most confident first.

    Ordering is by confidence so the cap is a *limitation* rule (best roads
    survive) rather than an arbitrary truncation of graph-iteration order.
    Without a cap the original edge order is preserved.
    """
    if max_features is not None and max_features > 0:
        candidates.sort(key=lambda item: item[0], reverse=True)
        return candidates[:max_features]
    return candidates


def mask_to_linestrings(
    prob_mask: np.ndarray,
    transform: rasterio.Affine,
    threshold: float = 0.5,
    *,
    min_length_m: float = 0.0,
    min_confidence: float = 0.0,
    max_features: int | None = None,
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
            logger.info("Skipping degenerate road edge", exc_info=True)
            continue

        # Road rules (limitation): each graph edge is already a skeleton
        # segment between junctions (degree >= 3 nodes), so separation across
        # intersections is inherent - edges never span a crossing. These pass
        # rules only *discard* edges that violate the cadastre conventions.
        if confidence < min_confidence:
            continue
        if _haversine_m(coords) < min_length_m:
            continue
        features.append((confidence, geometry))

    features = _apply_rule_cap(features, max_features)
    return [
        Feature(
            geometry=geometry,
            properties={
                "confidence": round(confidence, 4),
                "feature_type": "road",
            },
        )
        for confidence, geometry in features
    ]


def mask_to_polygons(
    prob_mask: np.ndarray,
    transform: rasterio.Affine,
    threshold: float = 0.5,
    *,
    min_confidence: float = 0.0,
    max_features: int | None = None,
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
            logger.info("Skipping malformed building polygon", exc_info=True)
            continue

        if not poly.is_valid or poly.area == 0 or poly.geom_type != "Polygon":
            continue

        confidence = float(prob_mask[rows, cols][region_mask].mean())
        if confidence < min_confidence:
            continue
        features.append((confidence, mapping(poly)))

    features = _apply_rule_cap(features, max_features)
    return [
        Feature(
            geometry=geometry,
            properties={
                "confidence": round(confidence, 4),
                "feature_type": "building",
            },
        )
        for confidence, geometry in features
    ]
