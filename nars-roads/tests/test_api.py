"""Endpoint-level tests that do not require torch.

The model is only built inside the lifespan context manager, so every test
here runs against the app with _model == None. That is enough to exercise
auth, validation, upload caps and the health/ready contract end to end."""

import pytest
from fastapi.testclient import TestClient
from helpers import AUTH_TOKEN, make_tiff_bytes, requires_torch

import app.main as roads
from app.main import app
from app.model import InvalidTileError, TileTooLargeError

client = TestClient(app)

AUTH = {"X-Internal-Token": AUTH_TOKEN}
BBOX = {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 1.0}


@pytest.fixture(autouse=True)
def _restore_module_globals():
    """Snapshot app.main's mutable, process-wide state and restore it after
    every test. A test that assigns roads._models / INFERENCE_SEMAPHORE / ...
    directly (instead of via monkeypatch) must not leak its mutation into the
    next test regardless of execution order."""
    snapshot = {
        name: getattr(roads, name)
        for name in (
            "_models",
            "INTERNAL_TOKEN",
            "MAX_TILE_BYTES",
            "INFERENCE_TIMEOUT",
            "QUEUE_TIMEOUT",
            "INFERENCE_SEMAPHORE",
        )
    }
    yield
    for name, value in snapshot.items():
        setattr(roads, name, value)


class _StubModel:
    """Minimal stand-in for SegmentationModel: satisfies the readiness gate
    (is_loaded) and lets the endpoint error paths be exercised without torch
    installed. predict raises whatever was configured for the test."""

    def __init__(self, is_loaded: bool = True, predict_error: Exception | None = None):
        self.is_loaded = is_loaded
        self._predict_error = predict_error

    def predict(self, raw: bytes, bbox: tuple[float, float, float, float]):
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
    # The readiness contract's success side: with real weights loaded the pod
    # reports ready and health reflects the loaded model. Mirrors the 503
    # paths tested elsewhere without needing torch (stub is enough).
    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel(is_loaded=True)})
    ready = client.get("/ready")
    assert ready.status_code == 200
    assert ready.json() == {"status": "ready", "model_loaded": True}
    assert client.get("/health").json() == {"status": "ok", "model_loaded": True}


def _ok_spec() -> roads.ModelSpec:
    return {"weights_path": "nope.pth", "num_classes": 2}


def test_load_model_constructs_healthy_model(monkeypatch):
    # The healthy path: a valid checkpoint constructs and returns a loaded model.
    monkeypatch.setattr(
        roads, "SegmentationModel", lambda *a, **k: _StubModel(is_loaded=True)
    )
    loaded = roads._load_model("buildings", _ok_spec())
    assert loaded is not None and loaded.is_loaded


def test_load_model_isolates_a_failed_task(monkeypatch):
    # Per-model fault isolation (fix #1): a checkpoint that fails to load must
    # not abort the whole service. `_load_model` logs and returns None so the
    # startup loop can skip that task while the others keep serving.
    monkeypatch.setattr(
        roads,
        "SegmentationModel",
        lambda *a, **k: (_ for _ in ()).throw(RuntimeError("corrupt")),
    )
    assert roads._load_model("buildings", _ok_spec()) is None


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


def test_segment_rejects_out_of_range_max_lon():
    # Per-axis validation (fix #5): the message names the offending axis and
    # bound instead of a merged "bbox is invalid".
    resp = _post(
        headers=AUTH,
        params={"min_lon": 0.0, "min_lat": 0.0, "max_lon": 181.0, "max_lat": 1.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422
    assert "max_lon" in resp.json()["detail"]


def test_segment_rejects_out_of_range_max_lat():
    resp = _post(
        headers=AUTH,
        params={"min_lon": 0.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 91.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422
    assert "max_lat" in resp.json()["detail"]


def test_segment_rejects_out_of_range_min_lon():
    resp = _post(
        headers=AUTH,
        params={"min_lon": -181.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 1.0},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422
    assert "min_lon" in resp.json()["detail"]


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
    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel(is_loaded=False)})
    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")},
    )
    assert resp.status_code == 503


def test_segment_returns_413_when_decode_too_large(monkeypatch):
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


def test_segment_returns_504_when_inference_times_out(monkeypatch):
    """A pathological tile that hangs the model must not hold a semaphore slot
    forever: the endpoint enforces a wall-clock timeout and answers 504."""
    import time

    class _HangingModel:
        is_loaded = True

        def predict(self, raw: bytes, bbox: tuple[float, float, float, float]):
            time.sleep(2)  # simulate a hang; short enough not to slow the suite
            raise AssertionError("test bug: predict should time out before returning")  # noqa: TRY003 - test fixture message

    monkeypatch.setattr(roads, "INFERENCE_TIMEOUT", 0.05)
    monkeypatch.setattr(roads, "_models", {"buildings": _HangingModel()})

    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", make_tiff_bytes(), "image/tiff")},
    )
    assert resp.status_code == 504
    assert "Inference did not complete within" in resp.json()["detail"]


def test_segment_schema_rejects_malformed_feature():
    from pydantic import ValidationError

    from app.schemas import SegmentResponse

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


def test_inference_pool_workers_are_daemon():
    """Regression for the shutdown-hang finding: workers must be daemon so a
    predict abandoned by the 504 timeout path never blocks interpreter exit
    (ThreadPoolExecutor's are non-daemon and joined at process shutdown)."""
    import threading
    import time

    from app.main import _InferenceExecutor

    executor = _InferenceExecutor(
        max_workers=1, thread_name_prefix="infer-daemon-regression"
    )
    future = executor.submit(lambda: time.sleep(0.05) or "ran")
    assert future.result(timeout=5) == "ran"

    workers = [
        t
        for t in threading.enumerate()
        if t.name.startswith("infer-daemon-regression") and t.is_alive()
    ]
    assert workers
    assert all(worker.daemon for worker in workers)


def test_queue_timeout_is_configured():
    # The capacity-gate wait is env-tunable like the other knobs; it must
    # parse to a sane integer so requests fail fast at capacity, not hang.
    assert isinstance(roads.QUEUE_TIMEOUT, int)
    assert 0 <= roads.QUEUE_TIMEOUT < 300


def test_segment_returns_503_when_semaphore_exhausted(monkeypatch):
    """When all inference permits are held, new requests must fail fast with
    503 instead of blocking indefinitely in the threadpool."""
    import threading
    from unittest.mock import MagicMock

    # Replace the semaphore with a mock whose acquire() always returns False
    # (simulates timeout). The original is restored by monkeypatch cleanup.
    mock_sema = MagicMock(spec=threading.BoundedSemaphore)
    mock_sema.acquire.return_value = False
    monkeypatch.setattr(roads, "INFERENCE_SEMAPHORE", mock_sema)
    monkeypatch.setattr(roads, "_models", {"buildings": _StubModel()})

    resp = _post(
        headers=AUTH,
        params=BBOX,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 503
    mock_sema.acquire.assert_called_once()


def test_missing_token_fails_closed(monkeypatch):
    # If the token env is missing entirely, all requests are rejected.
    monkeypatch.setattr(roads, "INTERNAL_TOKEN", "")
    assert _post(headers={"X-Internal-Token": AUTH_TOKEN}).status_code == 401
    assert _post().status_code == 401


def test_non_ascii_token_rejected_not_500():
    """A header byte > 0x7F reaches the app latin-1-decoded ("tok\\xe9" ->
    "toké"). secrets.compare_digest(str, str) raises TypeError on non-ASCII,
    which used to escape the dependency as an unhandled 500; the comparison
    runs on UTF-8 bytes, so this must be a clean 401. The header is passed
    as raw bytes because httpx itself refuses non-ASCII str values."""
    resp = _post(headers={"X-Internal-Token": b"tok\xe9"})
    assert resp.status_code == 401


def test_segment_rejects_oversized_upload(monkeypatch):
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
def test_segment_end_to_end_returns_geojson(monkeypatch: pytest.MonkeyPatch):
    """Full request path with the model actually loaded. The test image has no
    checkpoint, so weights are random and is_loaded is False — flip it to True
    to pass the fail-closed readiness gate while still exercising the whole
    predict -> postprocess -> response pipeline."""
    from app.main import app as live_app

    with TestClient(live_app) as live:
        assert roads._models["buildings"] is not None
        # Use monkeypatch so the mutation is scoped to this test and doesn't
        # leak into later tests that verify the default is_loaded=False.
        monkeypatch.setattr(roads._models["buildings"], "is_loaded", True)
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


# ── Boundary-value tests for validation helpers ────────────────────────────


@pytest.mark.parametrize(
    "params",
    [
        {"min_lon": -180.0, "min_lat": -90.0, "max_lon": 180.0, "max_lat": 90.0},
        {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 180.0, "max_lat": 90.0},
        {"min_lon": -180.0, "min_lat": -90.0, "max_lon": 0.0, "max_lat": 0.0},
    ],
    ids=["full-world", "origin-to-max", "min-to-origin"],
)
def test_segment_accepts_valid_bbox_boundaries(params):
    """Exact boundary values (-180/180, -90/90) must pass validation.
    The request still fails 503 (model not loaded), which proves the
    bbox was accepted."""
    resp = _post(
        headers=AUTH,
        params=params,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 503


@pytest.mark.parametrize(
    "params",
    [
        {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 0.0, "max_lat": 1.0},
        {"min_lon": 0.0, "min_lat": 0.0, "max_lon": 1.0, "max_lat": 0.0},
    ],
    ids=["equal-lon", "equal-lat"],
)
def test_segment_rejects_equal_bbox_boundaries(params):
    """min >= max on either axis must be rejected (the source uses >=, not >)."""
    resp = _post(
        headers=AUTH,
        params=params,
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


@pytest.mark.parametrize(
    "threshold",
    [0.0, 0.5, 1.0],
    ids=["zero", "default", "one"],
)
def test_segment_accepts_valid_threshold_boundaries(threshold):
    """0.0 and 1.0 are inclusive boundaries and must pass validation."""
    resp = _post(
        headers=AUTH,
        params={**BBOX, "threshold": threshold},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 503


@pytest.mark.parametrize(
    "threshold",
    [-0.001, 1.001],
    ids=["just-below-zero", "just-above-one"],
)
def test_segment_rejects_threshold_out_of_boundaries(threshold):
    resp = _post(
        headers=AUTH,
        params={**BBOX, "threshold": threshold},
        files={"tile": ("t.tif", b"x", "image/tiff")},
    )
    assert resp.status_code == 422


def test_none_token_fails_closed(monkeypatch):
    """When INTERNAL_TOKEN is None (env var unset), all requests are rejected.
    Distinct from the empty-string case: the source checks `not INTERNAL_TOKEN`
    which must handle both falsy values identically."""
    monkeypatch.setattr(roads, "INTERNAL_TOKEN", None)
    assert _post(headers={"X-Internal-Token": AUTH_TOKEN}).status_code == 401
    assert _post().status_code == 401
