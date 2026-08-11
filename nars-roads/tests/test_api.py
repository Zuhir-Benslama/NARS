"""Endpoint-level tests that do not require torch.

The model is only built inside the lifespan context manager, so every test
here runs against the app with _model == None. That is enough to exercise
auth, validation, upload caps and the health/ready contract end to end."""

import pytest
from fastapi.testclient import TestClient

from app.main import app
from conftest import make_tiff_bytes

try:
    import torch  # noqa: F401

    TORCH_AVAILABLE = True
except ImportError:
    TORCH_AVAILABLE = False

client = TestClient(app)

AUTH = {"X-Internal-Token": "test-token"}
BBOX = {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 1.0}


def _post(**kwargs):
    return client.post("/segment", **kwargs)


def test_health_reports_model_not_loaded():
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok", "model_loaded": False}


def test_ready_503_without_model():
    assert client.get("/ready").status_code == 503


def test_segment_rejects_missing_token():
    assert _post().status_code == 401


def test_segment_rejects_wrong_token():
    assert _post(headers={"X-Internal-Token": "nope"}).status_code == 401


def test_segment_missing_bbox_params():
    resp = _post(
        headers=AUTH, files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")}
    )
    assert resp.status_code == 422


def test_segment_rejects_unsupported_content_type():
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.txt", b"not an image", "text/plain")},
    )
    assert resp.status_code == 415


def test_segment_rejects_empty_file(monkeypatch):
    import app.main as roads

    # Satisfy the readiness gate so the empty-upload check is what fires.
    monkeypatch.setattr(roads, "_model", object())
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"", "image/tiff")},
    )
    assert resp.status_code == 400


def test_segment_rejects_threshold_out_of_range():
    resp = _post(
        headers=AUTH,
        params={**BBOX, "threshold": 1.5},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_segment_rejects_inverted_bbox():
    resp = _post(
        headers=AUTH,
        params={"min_lon": 1.0, "min_lat": 1.0, "max_lon": 0.0, "max_lat": 0.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_segment_rejects_out_of_range_latitude():
    resp = _post(
        headers=AUTH,
        params={"min_lon": 0.0, "min_lat": -95.0, "max_lon": 1.0, "max_lat": 1.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_segment_rejects_out_of_range_longitude():
    resp = _post(
        headers=AUTH,
        params={"min_lon": -200.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 1.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_segment_rejects_degenerate_bbox():
    resp = _post(
        headers=AUTH,
        params={"min_lon": 0.0, "min_lat": 0.0, "max_lon": 0.0, "max_lat": 1.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_segment_accepts_threshold_in_range():
    resp = _post(
        headers=AUTH,
        params={**BBOX, "threshold": 0.25},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 503


def test_segment_models_not_ready():
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 503


def test_segment_schema_rejects_malformed_feature():
    from pydantic import ValidationError

    from app.schemas import SegmentResponse

    with pytest.raises(ValidationError):
        SegmentResponse.model_validate(
            {
                "roads": {
                    "type": "FeatureCollection",
                    "features": [{"geometry": {"coordinates": []}}],  # missing type
                },
                "buildings": {"type": "FeatureCollection", "features": []},
            }
        )


def test_segment_schema_accepts_wellformed_features():
    from app.schemas import SegmentResponse

    parsed = SegmentResponse.model_validate(
        {
            "roads": {
                "type": "FeatureCollection",
                "features": [
                    {
                        "type": "Feature",
                        "geometry": {
                            "type": "LineString",
                            "coordinates": [[0, 0], [1, 1]],
                        },
                        "properties": {"confidence": 0.9, "feature_type": "road"},
                    }
                ],
            },
            "buildings": {"type": "FeatureCollection", "features": []},
        }
    )
    assert parsed.roads.features[0].geometry.type == "LineString"
    assert parsed.roads.features[0].properties["confidence"] == 0.9


def test_inference_concurrency_is_bounded():
    import threading

    import app.main as roads

    assert isinstance(roads.INFERENCE_SEMAPHORE, threading.BoundedSemaphore)
    # Behavioral check that doesn't reach into private _value: the semaphore
    # must not grant more than 2 concurrent permits (a third acquire fails).
    acquired = []
    try:
        for _ in range(3):
            acquired.append(roads.INFERENCE_SEMAPHORE.acquire(blocking=False))
        assert False in acquired, "semaphore allowed unbounded concurrency"
    finally:
        for _ in range(sum(acquired)):
            roads.INFERENCE_SEMAPHORE.release()


def test_missing_token_fails_closed(monkeypatch):
    # If the token env is missing entirely, all requests are rejected.
    import app.main as roads

    monkeypatch.setattr(roads, "INTERNAL_TOKEN", "")
    assert _post(headers={"X-Internal-Token": "test-token"}).status_code == 401
    assert _post().status_code == 401


def test_segment_rejects_oversized_upload(monkeypatch):
    import app.main as roads

    monkeypatch.setattr(roads, "MAX_TILE_BYTES", 1024)
    monkeypatch.setattr(roads, "_model", object())  # pass the readiness gate
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x" * 2048, "image/tiff")},
    )
    assert resp.status_code == 413


@pytest.mark.skipif(not TORCH_AVAILABLE, reason="torch not installed")
def test_segment_end_to_end_returns_geojson():
    """Full request path with the model actually loaded (random weights in the
    test image, since no checkpoint is mounted)."""
    from app.main import app as live_app

    with TestClient(live_app) as live:
        resp = live.post(
            "/segment",
            params={**BBOX, "threshold": 0.5},
            headers=AUTH,
            files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")},
        )
        assert resp.status_code == 200
        data = resp.json()
        assert set(data) == {"roads", "buildings"}
        for fc in data.values():
            assert fc["type"] == "FeatureCollection"
            assert isinstance(fc["features"], list)
            for feature in fc["features"]:
                assert feature["type"] == "Feature"
                assert feature["geometry"]["type"] in {"LineString", "Polygon"}
                assert "coordinates" in feature["geometry"]
                assert feature["properties"]["confidence"] >= 0.0
                assert feature["properties"]["confidence"] <= 1.0
