"""
Mask -> vector conversion.

Roads:     threshold -> clean -> skeletonize -> graph -> simplified LineStrings
Buildings: threshold -> clean -> connected components -> simplified Polygons
"""

import logging

import numpy as np
import rasterio
from shapely.errors import GEOSException
from shapely.geometry import LineString, Polygon, mapping

from app.schemas import Feature

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
    from skimage.measure import find_contours, label
    from skimage.morphology import closing, remove_small_objects

    binary = prob_mask > threshold
    binary = closing(binary)
    binary = remove_small_objects(binary, max_size=MIN_BUILDING_COMPONENT_PX - 1)
    if not binary.any():
        return []

    labeled = label(binary)

    features = []
    for region_id in range(1, labeled.max() + 1):
        region_mask = labeled == region_id
        contours = find_contours(region_mask.astype(np.float32), 0.5)
        if not contours:
            continue

        contour = max(contours, key=len)
        coords = [
            rasterio.transform.xy(transform, float(r), float(c)) for r, c in contour
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

        confidence = float(prob_mask[region_mask].mean())
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
