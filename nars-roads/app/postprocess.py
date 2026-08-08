"""
Mask -> vector conversion.

Roads:     threshold -> clean -> skeletonize -> graph -> simplified LineStrings
Buildings: threshold -> clean -> connected components -> simplified Polygons
"""

import numpy as np
import rasterio
import sknw
from shapely.geometry import LineString, Polygon, mapping
from skimage.measure import find_contours, label
from skimage.morphology import (
    binary_closing,
    remove_small_objects,
    skeletonize,
)

# Simplification tolerance in degrees. ~0.00002 deg is roughly 2m at the
# equator - tune per your imagery resolution.
SIMPLIFY_TOLERANCE = 0.00002
MIN_ROAD_COMPONENT_PX = 40
MIN_BUILDING_COMPONENT_PX = 20


def mask_to_linestrings(
    prob_mask: np.ndarray, transform: rasterio.Affine, threshold: float = 0.5
) -> list[dict]:
    binary = prob_mask > threshold
    binary = remove_small_objects(binary, min_size=MIN_ROAD_COMPONENT_PX)
    if not binary.any():
        return []

    skeleton = skeletonize(binary)
    graph = sknw.build_sknw(skeleton, multi=True)

    features = []
    for s, e, edge_data in graph.edges(data=True):
        pts = edge_data.get("pts")
        if pts is None or len(pts) < 2:
            continue

        coords = [rasterio.transform.xy(transform, float(r), float(c)) for r, c in pts]
        line = LineString(coords)
        if line.length == 0:
            continue
        line = line.simplify(SIMPLIFY_TOLERANCE, preserve_topology=False)

        confidence = float(prob_mask[pts[:, 0], pts[:, 1]].mean())
        features.append(
            {
                "type": "Feature",
                "geometry": mapping(line),
                "properties": {
                    "confidence": round(confidence, 4),
                    "feature_type": "road",
                },
            }
        )
    return features


def mask_to_polygons(
    prob_mask: np.ndarray, transform: rasterio.Affine, threshold: float = 0.5
) -> list[dict]:
    binary = prob_mask > threshold
    binary = binary_closing(binary)
    binary = remove_small_objects(binary, min_size=MIN_BUILDING_COMPONENT_PX)
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
        except Exception:
            continue

        if not poly.is_valid or poly.area == 0 or poly.geom_type != "Polygon":
            continue

        confidence = float(prob_mask[region_mask].mean())
        features.append(
            {
                "type": "Feature",
                "geometry": mapping(poly),
                "properties": {
                    "confidence": round(confidence, 4),
                    "feature_type": "building",
                },
            }
        )
    return features
