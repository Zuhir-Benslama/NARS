"""Mask -> vector conversion tests. Requires scikit-image and sknw, so the
module is skipped entirely when they are not installed."""

import numpy as np
import pytest
from rasterio.transform import Affine

pytest.importorskip("skimage")
pytest.importorskip("sknw")

import networkx as nx

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


def _fake_graph(edges):
    """A networkx graph with edges carrying crafted sknw-style `pts` payloads,
    so the degenerate-edge guards in mask_to_linestrings can be exercised
    deterministically instead of hoping sknw produces pathological output."""
    graph = nx.Graph()
    for i, pts in enumerate(edges):
        graph.add_edge(f"n{i}", f"n{i + 1}", pts=pts)
    return graph


def _patch_graph(monkeypatch, edges):
    import sknw

    monkeypatch.setattr(sknw, "build_sknw", lambda *args, **kwargs: _fake_graph(edges))


def test_linestrings_skips_edge_without_pts(monkeypatch):
    # sknw can attach no `pts` payload to an edge; the guard must skip it.
    _patch_graph(monkeypatch, [None])
    assert mask_to_linestrings(_road_mask(), TRANSFORM) == []


def test_linestrings_skips_single_point_edge(monkeypatch):
    # A one-point edge cannot form a line.
    _patch_graph(monkeypatch, [[(10, 10)]])
    assert mask_to_linestrings(_road_mask(), TRANSFORM) == []


def test_linestrings_skips_zero_length_edge(monkeypatch):
    # Duplicate points collapse to a zero-length line.
    _patch_graph(monkeypatch, [[(10, 10), (10, 10)]])
    assert mask_to_linestrings(_road_mask(), TRANSFORM) == []


def test_linestrings_skips_invalid_line(monkeypatch):
    # shapely 2.x's is_valid does not reject self-crossing LineStrings (GEOS
    # only checks simplicity separately), so force the defensive guard to
    # prove that a geometry flagged invalid is skipped, not emitted.
    from shapely.geometry import LineString

    _patch_graph(monkeypatch, [[(0, 0), (5, 5), (0, 5), (5, 0)]])
    monkeypatch.setattr(LineString, "is_valid", False)
    assert mask_to_linestrings(_road_mask(), TRANSFORM) == []


def test_linestrings_skips_edge_that_raises(monkeypatch):
    # A geometry-construction failure must skip the edge, not 500 the request.
    import rasterio

    def boom(transform, row, col):
        raise ValueError("degenerate transform")  # noqa: TRY003 - dynamic message

    monkeypatch.setattr(rasterio.transform, "xy", boom)
    _patch_graph(monkeypatch, [[(3, 4), (5, 6)]])
    assert mask_to_linestrings(_road_mask(), TRANSFORM) == []


def test_linestrings_mixed_edges_keeps_valid(monkeypatch):
    # Degenerate edges must be skipped while the valid one is still emitted.
    _patch_graph(
        monkeypatch,
        [
            None,  # no pts
            [(7, 7)],  # single point
            [(10, 10), (10, 10)],  # zero length
            [(3, 3), (4, 4), (5, 5)],  # valid
        ],
    )
    features = mask_to_linestrings(_road_mask(), TRANSFORM)
    assert len(features) == 1
    assert features[0].geometry.type == "LineString"
    assert features[0].properties["feature_type"] == "road"
    assert 0.0 <= features[0].properties["confidence"] <= 1.0


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


def test_polygons_skips_short_contour(monkeypatch):
    # A contour with fewer than 4 points cannot bound a polygon; the region
    # must be skipped, not crash the request. mask_to_polygons imports
    # find_contours locally, so patch it at the source.
    import skimage.measure

    monkeypatch.setattr(
        skimage.measure,
        "find_contours",
        lambda *args, **kwargs: [[(10, 10), (11, 11), (12, 12)]],
    )
    assert mask_to_polygons(_building_mask(), TRANSFORM) == []


def test_polygons_skips_region_without_contours(monkeypatch):
    # find_contours can return nothing for a labeled region; the region must
    # be skipped rather than treated as an empty polygon.
    import skimage.measure

    monkeypatch.setattr(skimage.measure, "find_contours", lambda *args, **kwargs: [])
    assert mask_to_polygons(_building_mask(), TRANSFORM) == []


def test_polygons_skips_region_that_raises(monkeypatch):
    # A polygon-construction failure must skip the region (mirrors the
    # linestring edge guard) instead of 500ing the whole request.
    import app.postprocess as roads_postprocess

    def boom(coords):
        raise ValueError("degenerate polygon")  # noqa: TRY003 - dynamic message

    monkeypatch.setattr(roads_postprocess, "Polygon", boom)
    assert mask_to_polygons(_building_mask(), TRANSFORM) == []


def test_polygons_skips_invalid_polygon(monkeypatch):
    # A constructed polygon flagged invalid must be dropped rather than
    # emitted as garbage geometry. shapely 2.x does not always reject these
    # via is_valid (see the LineString note above), so force the guard.
    from shapely.geometry import Polygon

    monkeypatch.setattr(Polygon, "is_valid", False)
    assert mask_to_polygons(_building_mask(), TRANSFORM) == []


def test_polygons_bad_region_keeps_valid_one(monkeypatch):
    # One degenerate region must not prevent the valid region's polygon from
    # being emitted — the guard's reason to exist.
    import skimage.measure

    from skimage.measure import find_contours as real_find_contours

    prob = np.zeros((60, 60), dtype=np.float32)
    prob[5:25, 5:25] = 0.9  # top square -> forced short contour (skipped)
    prob[35:55, 35:55] = 0.9  # bottom square -> real contour (kept)

    def fake_find_contours(region_mask, level=0.5):
        if int(np.argwhere(region_mask)[0][0]) < 30:
            return [[(10, 10), (11, 11), (12, 12)]]
        return real_find_contours(region_mask.astype(np.float32), level)

    monkeypatch.setattr(skimage.measure, "find_contours", fake_find_contours)
    features = mask_to_polygons(prob, TRANSFORM)
    assert len(features) == 1
    assert features[0].geometry.type == "Polygon"
