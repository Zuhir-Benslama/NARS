"""Model tests.

The numpy-only normalization tests run anywhere. The torch-dependent tests
(a real forward pass) are skipped when torch is not installed so the suite
stays runnable on machines without the ML stack.
"""

import numpy as np
import pytest
from rasterio.transform import Affine, from_bounds

from app.model import SegmentationModel, TileTooLargeError
from conftest import make_tiff_bytes

try:
    import torch  # noqa: F401

    TORCH_AVAILABLE = True
except ImportError:
    TORCH_AVAILABLE = False

skip_without_torch = pytest.mark.skipif(
    not TORCH_AVAILABLE, reason="torch not installed"
)


def _normalize(arr, dtype):
    return SegmentationModel._normalize_window(arr, np.dtype(dtype))


def test_normalize_window_uint8_scales_to_unit():
    arr = np.zeros((3, 2, 4), dtype=np.uint8)
    arr[1, 0, 1] = 255
    arr[2, 1, 3] = 128
    out = _normalize(arr, "uint8")
    assert out.shape == (2, 4, 3)
    assert out.dtype == np.float32
    assert out[0, 1, 1] == pytest.approx(1.0)
    assert out[1, 3, 2] == pytest.approx(128 / 255)
    assert out.min() >= 0.0 and out.max() <= 1.0


def test_normalize_window_uint16_scales_by_bit_depth():
    arr = np.zeros((3, 1, 1), dtype=np.uint16)
    arr[:, 0, 0] = 65535
    out = _normalize(arr, "uint16")
    assert out[0, 0, 0] == pytest.approx(1.0)


def test_normalize_window_float_normalized_unchanged():
    arr = np.full((3, 1, 1), 0.7, dtype=np.float32)
    out = _normalize(arr, "float32")
    assert out[0, 0, 0] == pytest.approx(0.7)


def test_normalize_window_float_0_255_rescaled():
    arr = np.full((3, 1, 1), 200.0, dtype=np.float32)
    out = _normalize(arr, "float32")
    assert out[0, 0, 0] == pytest.approx(200.0 / 255.0)


def test_normalize_window_single_band_repeated():
    arr = np.array([[[10, 20]]], dtype=np.uint8)  # (1, 1, 2)
    out = _normalize(arr, "uint8")
    assert out.shape == (1, 2, 3)
    assert np.allclose(out[0, 0, :], 10 / 255)
    assert np.allclose(out[0, 1, :], 20 / 255)


def test_normalize_window_trims_extra_bands():
    arr = np.zeros((4, 1, 1), dtype=np.uint8)
    out = _normalize(arr, "uint8")
    assert out.shape == (1, 1, 3)


@pytest.fixture(scope="module")
def model():
    return SegmentationModel(weights_path="/nonexistent/weights.pth", tile_size=32)


@skip_without_torch
def test_predict_shapes_and_georeferenced_transform(model):
    raw = make_tiff_bytes(width=64, height=48)
    road, building, transform = model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
    assert road.shape == (48, 64)
    assert building.shape == (48, 64)
    assert road.dtype == np.float32
    assert road.min() >= 0.0 and road.max() <= 1.0
    assert building.min() >= 0.0 and building.max() <= 1.0
    assert transform == Affine(1.0, 0.0, 100.0, 0.0, -1.0, 50.0)


@skip_without_torch
def test_predict_bbox_fallback_when_not_georeferenced(model):
    raw = make_tiff_bytes(width=64, height=48, transform=Affine.identity())
    _, _, transform = model.predict(raw, bbox=(10.0, 20.0, 12.0, 21.0))
    expected = from_bounds(10.0, 20.0, 12.0, 21.0, width=64, height=48)
    assert transform == expected


@skip_without_torch
def test_predict_decodes_windows_not_whole_image(model, monkeypatch):
    raw = make_tiff_bytes(width=100, height=80)
    seen = []

    def fake_predict(chip):
        seen.append(chip.shape[:2])
        return np.broadcast_to(
            np.array([0.0, 0.9, 0.1], dtype=np.float32),
            chip.shape[:2] + (3,),
        ).copy()

    monkeypatch.setattr(model, "_predict_tile", fake_predict)
    road, building, _ = model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
    # 80x100 with tile_size=32 -> rows 32/32/16, cols 32/32/32/4
    assert set(seen) == {(32, 32), (32, 4), (16, 32), (16, 4)}
    assert road.shape == (80, 100)
    assert np.allclose(road, 0.9)
    assert np.allclose(building, 0.1)


@skip_without_torch
def test_predict_rejects_decode_over_budget(model, monkeypatch):
    import app.model as roads_model

    monkeypatch.setattr(roads_model, "MAX_DECODED_PIXELS", 100)
    raw = make_tiff_bytes(width=64, height=48)  # 3072 pixels > 100
    with pytest.raises(TileTooLargeError):
        model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
