"""Mask -> vector conversion tests. Requires scikit-image and sknw, so the
module is skipped entirely when they are not installed."""

import numpy as np
import pytest
from rasterio.transform import Affine

pytest.importorskip("skimage")
pytest.importorskip("sknw")

from app.postprocess import mask_to_linestrings, mask_to_polygons

# pixel (row, col) maps to (x=col, y=row)
TRANSFORM = Affine(1.0, 0.0, 0.0, 0.0, 1.0, 0.0)


def _road_mask(height=50, width=50):
    prob = np.zeros((height, width), dtype=np.float32)
    prob[10, 5:45] = 0.9
    return prob


def _building_mask(height=60, width=60):
    prob = np.zeros((height, width), dtype=np.float32)
    prob[20:40, 20:40] = 0.9
    return prob


def test_linestrings_empty_mask():
    assert not mask_to_linestrings(np.zeros((20, 20), dtype=np.float32), TRANSFORM)


def test_linestrings_below_threshold():
    assert not mask_to_linestrings(_road_mask(), TRANSFORM, threshold=0.95)


def test_linestrings_horizontal_line():
    features = mask_to_linestrings(_road_mask(), TRANSFORM)
    assert len(features) >= 1
    for feature in features:
        assert feature.type == "Feature"
        assert feature.geometry.type == "LineString"
        assert feature.properties["feature_type"] == "road"
        assert 0.0 <= feature.properties["confidence"] <= 1.0
        # the line sits on row 10; sknw centers coordinates on pixel centers
        ys = [coord[1] for coord in feature.geometry.coordinates]
        assert all(abs(y - 10.5) < 0.5 for y in ys)


def test_polygons_empty_mask():
    assert not mask_to_polygons(np.zeros((20, 20), dtype=np.float32), TRANSFORM)


def test_polygons_below_threshold():
    assert not mask_to_polygons(_building_mask(), TRANSFORM, threshold=0.95)


def test_polygons_square_blob():
    features = mask_to_polygons(_building_mask(), TRANSFORM)
    assert len(features) >= 1
    for feature in features:
        assert feature.type == "Feature"
        assert feature.geometry.type == "Polygon"
        assert feature.properties["feature_type"] == "building"
        assert 0.0 <= feature.properties["confidence"] <= 1.0
        xs = [coord[0] for coord in feature.geometry.coordinates[0]]
        ys = [coord[1] for coord in feature.geometry.coordinates[0]]
        assert min(xs) >= 18
        assert max(xs) <= 42
        assert min(ys) >= 18
        assert max(ys) <= 42
