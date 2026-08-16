"""Endpoint-level tests that do not require torch.

The model is only built inside the lifespan context manager, so every test
here runs against the app with _model == None. That is enough to exercise
auth, validation, upload caps and the health/ready contract end to end."""

import pytest
from app.main import app
from app.model import InvalidTileError, TileTooLargeError
from fastapi.testclient import TestClient
from helpers import AUTH_TOKEN, make_tiff_bytes, requires_torch

client = TestClient(app)

AUTH = {"X-Internal-Token": AUTH_TOKEN}
BBOX = {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 1.0}


class _StubModel:
    """Minimal stand-in for SegmentationModel: satisfies the readiness gate
    (is_loaded) and lets the endpoint error paths be exercised without torch
    installed. predict raises whatever was configured for the test."""

    def __init__(self, is_loaded: bool = True, predict_error: Exception | None = None):
        self.is_loaded = is_loaded
        self._predict_error = predict_error

    def predict(self, raw, bbox):
        if self._predict_error is not None:
            raise self._predict_error
        raise AssertionError("test bug: predict should not be reached")  # noqa: TRY003 - dynamic message


def _post(**kwargs):
    return client.post("/segment/buildings", **kwargs)


def test_health_reports_model_not_loaded():
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok", "model_loaded": False}


def test_ready_503_without_model():
    assert client.get("/ready").status_code == 503


def test_ready_200_and_health_when_model_loaded(monkeypatch):
    import app.main as roads

    # The readiness contract's success side: with real weights loaded the pod
    # reports ready and health reflects the loaded model. Mirrors the 503
    # paths tested elsewhere without needing torch (stub is enough).
    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel(is_loaded=True)})
    ready = client.get("/ready")
    assert ready.status_code == 200
    assert ready.json() == {"status": "ready", "model_loaded": True}
    assert client.get("/health").json() == {"status": "ok", "model_loaded": True}


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
    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel()})
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


def test_segment_rejects_unready_model(monkeypatch):
    # Constructed model whose weights never loaded must fail closed (mirrors
    # /ready) instead of serving random predictions.
    import app.main as roads

    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel(is_loaded=False)})
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")},
    )
    assert resp.status_code == 503


def test_segment_returns_413_when_decode_too_large(monkeypatch):
    import app.main as roads

    monkeypatch.setattr(
        roads,
        "_models",
        {"buildings": _StubModel(predict_error=TileTooLargeError("too big"))},
    )
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 413


def test_segment_returns_400_for_undecodable_tile(monkeypatch):
    # Content-type says TIFF but the bytes are not a readable image; the
    # resulting decode error must surface as 400, not a 500.
    import app.main as roads

    monkeypatch.setattr(
        roads,
        "_models",
        {"buildings": _StubModel(predict_error=InvalidTileError("garbage tile"))},
    )
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"garbage", "image/tiff")},
    )
    assert resp.status_code == 400


def test_segment_returns_500_on_inference_failure(monkeypatch):
    import app.main as roads

    monkeypatch.setattr(
        roads,
        "_models",
        {"buildings": _StubModel(predict_error=RuntimeError("boom"))},
    )
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 500


def test_segment_schema_rejects_malformed_feature():
    from app.schemas import SegmentResponse
    from pydantic import ValidationError

    with pytest.raises(ValidationError):
        SegmentResponse.model_validate(
            {
                "buildings": {
                    "type": "FeatureCollection",
                    "features": [{"geometry": {"coordinates": []}}],  # missing type
                },
            }
        )


def test_segment_schema_accepts_wellformed_features():
    from app.schemas import SegmentResponse

    parsed = SegmentResponse.model_validate(
        {
            "buildings": {
                "type": "FeatureCollection",
                "features": [
                    {
                        "type": "Feature",
                        "geometry": {
                            "type": "Polygon",
                            "coordinates": [[[0, 0], [0, 1], [1, 1], [1, 0], [0, 0]]],
                        },
                        "properties": {"confidence": 0.9, "feature_type": "building"},
                    }
                ],
            },
        }
    )
    assert parsed.buildings.features[0].geometry.type == "Polygon"
    assert parsed.buildings.features[0].properties["confidence"] == 0.9


def test_inference_concurrency_is_bounded():
    import threading

    import app.main as roads

    assert isinstance(roads.INFERENCE_SEMAPHORE, threading.BoundedSemaphore)
    # Behavioral check that doesn't reach into private _value: the semaphore
    # must never grant more than MAX_CONCURRENT_INFERENCES permits (one more
    # acquire must fail). Derives the limit from the module instead of
    # hardcoding the default so raising the env cap doesn't break the test.
    limit = roads.MAX_CONCURRENT_INFERENCES
    acquired = [
        roads.INFERENCE_SEMAPHORE.acquire(blocking=False) for _ in range(limit + 1)
    ]
    try:
        assert False in acquired, "semaphore allowed unbounded concurrency"
    finally:
        for _ in range(sum(acquired)):
            roads.INFERENCE_SEMAPHORE.release()


def test_missing_token_fails_closed(monkeypatch):
    # If the token env is missing entirely, all requests are rejected.
    import app.main as roads

    monkeypatch.setattr(roads, "INTERNAL_TOKEN", "")
    assert _post(headers={"X-Internal-Token": AUTH_TOKEN}).status_code == 401
    assert _post().status_code == 401


def test_segment_rejects_oversized_upload(monkeypatch):
    import app.main as roads

    monkeypatch.setattr(roads, "MAX_TILE_BYTES", 1024)
    monkeypatch.setattr(
        roads, "_models", {"buildings": _StubModel()}
    )  # pass the readiness gate
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x" * 2048, "image/tiff")},
    )
    assert resp.status_code == 413


@requires_torch
def test_segment_end_to_end_returns_geojson():
    """Full request path with the model actually loaded. The test image has no
    checkpoint, so weights are random and is_loaded is False — flip it to True
    to pass the fail-closed readiness gate while still exercising the whole
    predict -> postprocess -> response pipeline."""
    import app.main as roads
    from app.main import app as live_app

    with TestClient(live_app) as live:
        assert roads._models["buildings"] is not None
        roads._models["buildings"].is_loaded = True
        resp = live.post(
            "/segment/buildings",
            params={**BBOX, "threshold": 0.5},
            headers=AUTH,
            files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")},
        )
        assert resp.status_code == 200
        data = resp.json()
        assert set(data) == {"buildings"}
        for fc in data.values():
            assert fc["type"] == "FeatureCollection"
            assert isinstance(fc["features"], list)
            for feature in fc["features"]:
                assert feature["type"] == "Feature"
                assert feature["geometry"]["type"] == "Polygon"
                assert "coordinates" in feature["geometry"]
                assert feature["properties"]["confidence"] >= 0.0
                assert feature["properties"]["confidence"] <= 1.0
