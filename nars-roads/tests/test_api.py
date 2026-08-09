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


def test_segment_rejects_empty_file():
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


def test_segment_rejects_oversized_upload(monkeypatch):
    import app.main as roads

    monkeypatch.setattr(roads, "MAX_TILE_BYTES", 1024)
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
