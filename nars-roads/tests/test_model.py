"""Model tests.

The numpy-only normalization tests run anywhere. The torch-dependent tests
(a real forward pass) are skipped when torch is not installed so the suite
stays runnable on machines without the ML stack.
"""

import numpy as np
import pytest
from helpers import DEFAULT_TRANSFORM, make_tiff_bytes, requires_torch
from rasterio.transform import Affine, from_bounds

from app.model import InvalidTileError, SegmentationModel, TileTooLargeError


def _normalize(arr):
    return SegmentationModel._normalize_window(arr)


def test_normalize_window_uint8_scales_to_unit():
    arr = np.zeros((3, 2, 4), dtype=np.uint8)
    arr[1, 0, 1] = 255
    arr[2, 1, 3] = 128
    out = _normalize(arr)
    assert out.shape == (2, 4, 3)
    assert out.dtype == np.float32
    assert out[0, 1, 1] == pytest.approx(1.0)
    assert out[1, 3, 2] == pytest.approx(128 / 255)
    assert out.min() >= 0.0
    assert out.max() <= 1.0


def test_normalize_window_uint16_scales_by_bit_depth():
    arr = np.zeros((3, 1, 1), dtype=np.uint16)
    arr[:, 0, 0] = 65535
    out = _normalize(arr)
    assert out[0, 0, 0] == pytest.approx(1.0)


def test_normalize_window_float_normalized_unchanged():
    arr = np.full((3, 1, 1), 0.7, dtype=np.float32)
    out = _normalize(arr)
    assert out[0, 0, 0] == pytest.approx(0.7)


def test_normalize_window_float_0_255_rescaled():
    arr = np.full((3, 1, 1), 200.0, dtype=np.float32)
    out = _normalize(arr)
    assert out[0, 0, 0] == pytest.approx(200.0 / 255.0)


def test_normalize_window_float_noise_above_one_not_rescaled():
    # A value just over 1.0 is noise on a [0,1] raster and must not be
    # divided by 255 (which would black it). Regression for the old
    # `img.max() > 1.0 -> /255` heuristic.
    arr = np.full((3, 1, 1), 1.02, dtype=np.float32)
    out = _normalize(arr)
    assert out[0, 0, 0] == pytest.approx(1.0)


def test_normalize_window_float_nan_and_inf_neutralized():
    # One pixel column with nan/+inf/-inf across the 3 bands, plus a 0.5 col.
    arr = np.array(
        [[[float("nan"), float("inf"), -float("inf"), 0.5]]], dtype=np.float32
    )
    arr = np.repeat(arr, 3, axis=0)  # (3, 1, 4) -> transpose makes W the middle dim
    out = _normalize(arr)
    assert np.all(np.isfinite(out))
    assert np.all(out[0, 0, :] == 0.0)  # nan -> 0
    assert np.all(out[0, 1, :] == 1.0)  # +inf -> 1
    assert np.all(out[0, 2, :] == 0.0)  # -inf -> 0
    assert np.all(out[0, 3, :] == pytest.approx(0.5))


def test_normalize_window_single_band_repeated():
    arr = np.array([[[10, 20]]], dtype=np.uint8)  # (1, 1, 2)
    out = _normalize(arr)
    assert out.shape == (1, 2, 3)
    assert np.allclose(out[0, 0, :], 10 / 255)
    assert np.allclose(out[0, 1, :], 20 / 255)


def test_normalize_window_trims_extra_bands():
    arr = np.zeros((4, 1, 1), dtype=np.uint8)
    out = _normalize(arr)
    assert out.shape == (1, 1, 3)


@pytest.fixture(scope="module")
def model():
    return SegmentationModel(weights_path="/nonexistent/weights.pth", tile_size=32)


@requires_torch
def test_predict_shapes_and_georeferenced_transform(model):
    raw = make_tiff_bytes(width=64, height=48)
    building, transform = model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
    assert building.shape == (48, 64)
    assert building.dtype == np.float32
    assert building.min() >= 0.0
    assert building.max() <= 1.0
    assert transform == DEFAULT_TRANSFORM


@requires_torch
def test_predict_bbox_fallback_when_not_georeferenced(model):
    raw = make_tiff_bytes(width=64, height=48, transform=Affine.identity())
    _, transform = model.predict(raw, bbox=(10.0, 20.0, 12.0, 21.0))
    expected = from_bounds(10.0, 20.0, 12.0, 21.0, width=64, height=48)
    assert transform == expected


@requires_torch
def test_predict_bbox_fallback_when_projected_crs(model):
    # A UTM (projected, meter-unit) transform must NOT be trusted: emitting it
    # as-is would produce meter-scale coordinates in an EPSG:4326 response.
    raw = make_tiff_bytes(width=64, height=48, crs="EPSG:32633")
    _, transform = model.predict(raw, bbox=(10.0, 20.0, 12.0, 21.0))
    expected = from_bounds(10.0, 20.0, 12.0, 21.0, width=64, height=48)
    assert transform == expected


@requires_torch
def test_predict_bbox_fallback_when_transform_has_no_crs(model):
    # A non-identity transform without an accompanying CRS is unverifiable;
    # the caller-supplied bbox wins over an opaque transform.
    raw = make_tiff_bytes(width=64, height=48, crs=None)
    _, transform = model.predict(raw, bbox=(10.0, 20.0, 12.0, 21.0))
    expected = from_bounds(10.0, 20.0, 12.0, 21.0, width=64, height=48)
    assert transform == expected


@requires_torch
def test_predict_decodes_windows_not_whole_image(model, monkeypatch):
    raw = make_tiff_bytes(width=100, height=80)
    seen = []

    def fake_predict(chip):
        seen.append(chip.shape[:2])
        return np.broadcast_to(
            np.array([0.1, 0.9], dtype=np.float32),
            (*chip.shape[:2], 2),
        ).copy()

    monkeypatch.setattr(model, "_predict_tile", fake_predict)
    building, _ = model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
    # 80x100 with tile_size=32 -> rows 32/32/16, cols 32/32/32/4
    assert set(seen) == {(32, 32), (32, 4), (16, 32), (16, 4)}
    assert building.shape == (80, 100)
    assert np.allclose(building, 0.9)


@requires_torch
def test_predict_rejects_decode_over_budget(model, monkeypatch):
    import app.model as roads_model

    monkeypatch.setattr(roads_model, "MAX_DECODED_PIXELS", 100)
    raw = make_tiff_bytes(width=64, height=48)  # 3072 pixels > 100
    with pytest.raises(TileTooLargeError):
        model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))


@requires_torch
def test_predict_rejects_non_image_bytes(model):
    # Bytes that are not a readable image must raise InvalidTileError so the
    # endpoint can map it to a 4xx instead of crashing with a 500.
    with pytest.raises(InvalidTileError):
        model.predict(b"definitely not a tiff", bbox=(0.0, 0.0, 1.0, 1.0))


@requires_torch
def test_weights_load_sets_is_loaded(tmp_path, model):
    # The happy path for checkpoint loading: a real state_dict on disk must
    # leave is_loaded True so /ready can report the pod as serviceable.
    import torch

    checkpoint = tmp_path / "weights.pth"
    torch.save(model.net.state_dict(), checkpoint)
    loaded = SegmentationModel(weights_path=str(checkpoint), tile_size=32)
    assert loaded.is_loaded
    assert loaded.net is not model.net
