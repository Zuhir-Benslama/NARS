"""Model tests.

The numpy-only normalization tests run anywhere. The torch-dependent tests
(a real forward pass) are skipped when torch is not installed so the suite
stays runnable on machines without the ML stack.
"""

import numpy as np
import pytest
from helpers import DEFAULT_TRANSFORM, make_tiff_bytes, requires_torch
from rasterio.io import MemoryFile
from rasterio.transform import Affine, from_bounds

from app.model import InvalidTileError, SegmentationModel, TileTooLargeError


def _normalize(arr, **kwargs):
    return SegmentationModel._normalize_window(arr, **kwargs)


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


def test_normalize_window_uint16_low_range_uses_raster_scale():
    # A low-range 16-bit product (DNs ~1500) must be scaled by its actual
    # range, not 65535, or it collapses to ~0 and comes out black.
    arr = np.zeros((3, 1, 1), dtype=np.uint16)
    arr[:, 0, 0] = 1500
    out = _normalize(arr, scale=1500.0)
    assert out[0, 0, 0] == pytest.approx(1.0)


def test_normalize_window_float_negatives_clipped():
    # Float rasters are not guaranteed padded to zero; negative pixels must
    # be clipped (was: only clipping when max > 1.0). (3,1,3) -> transpose
    # gives (H=1, W=3, bands=3): pixel columns are the middle dimension.
    arr = np.array([[[-0.5, 0.3, 0.8]]], dtype=np.float32)
    arr = np.repeat(arr, 3, axis=0)
    out = _normalize(arr)
    assert out[0, 0, 0] == 0.0
    assert out[0, 1, 0] == pytest.approx(0.3)
    assert out[0, 2, 0] == pytest.approx(0.8)


def test_integer_scale_probes_low_range_uint16():
    # _integer_scale derives the denominator from the raster's real data
    # range, so low-range 16-bit tiles normalize to something visible.
    data = np.zeros((3, 32, 32), dtype=np.uint16)
    data[1, 4:28, 4:28] = 1500
    with MemoryFile() as memfile:
        with memfile.open(
            driver="GTiff",
            width=32,
            height=32,
            count=3,
            dtype="uint16",
        ) as dst:
            dst.write(data)
        with memfile.open() as src:
            assert SegmentationModel._integer_scale(src) == pytest.approx(1500.0)


def test_integer_scale_full_depth_uses_dtype_max():
    # Data that actually spans the full 16-bit depth keeps the dtype max so
    # absolute radiometric range is preserved across differently-deep rasters.
    data = np.zeros((3, 32, 32), dtype=np.uint16)
    data[1, 4:28, 4:28] = 65535
    with MemoryFile() as memfile:
        with memfile.open(
            driver="GTiff",
            width=32,
            height=32,
            count=3,
            dtype="uint16",
        ) as dst:
            dst.write(data)
        with memfile.open() as src:
            assert SegmentationModel._integer_scale(src) == 65535.0


def test_integer_scale_float_raster_is_identity():
    # Float tiles self-normalize in _normalize_window; the scale must be a
    # harmless identity so predict() can always pass one denominator through.
    data = np.full((3, 16, 16), 0.5, dtype=np.float32)
    with MemoryFile() as memfile:
        with memfile.open(
            driver="GTiff",
            width=16,
            height=16,
            count=3,
            dtype="float32",
        ) as dst:
            dst.write(data)
        with memfile.open() as src:
            assert SegmentationModel._integer_scale(src) == 1.0


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


@pytest.fixture(scope="module")
def road_model():
    return SegmentationModel(
        weights_path="/nonexistent/roads.pth",
        tile_size=32,
        num_classes=1,
        builder="resnet34-upsample",
    )


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


# ── Roads builder: single-class sigmoid Resnet34Upsample ───────────────────


@requires_torch
def test_road_model_uses_resnet34_upsample_builder(road_model):
    # The roads spec maps to the vendored SpaceNet champion architecture, not
    # smp.Unet. The vendored class is transportable and its attribute names
    # match the converted checkpoint (convert_roads.py).
    from app.road_model import Resnet34Upsample

    assert road_model.builder == "resnet34-upsample"
    assert isinstance(road_model.net, Resnet34Upsample)
    # Single foreground logit shared by all roads pixels.
    assert road_model.net.final[0].out_channels == 1


@requires_torch
def test_road_model_uses_sigmoid_not_softmax(road_model):
    # Softmax over one logit is identically 1.0, which would fake total
    # certainty; the roads head must use sigmoid so probabilities stay in
    # (0, 1).
    assert road_model.activation == "sigmoid"


@requires_torch
def test_road_predict_shapes_and_probability_range(road_model):
    raw = make_tiff_bytes(width=64, height=48)
    road, transform = road_model.predict(raw, bbox=(0.0, 0.0, 1.0, 1.0))
    assert road.shape == (48, 64)
    assert road.dtype == np.float32
    assert road.min() >= 0.0
    assert road.max() <= 1.0
    assert transform == DEFAULT_TRANSFORM


@requires_torch
def test_road_weights_load_sets_is_loaded(tmp_path, road_model):
    # Roads checkpoints produced by convert_roads.py load strict=True through
    # the same SegmentationModel code path as buildings checkpoints.
    import torch

    checkpoint = tmp_path / "roads_best.pth"
    torch.save(road_model.net.state_dict(), checkpoint)
    loaded = SegmentationModel(
        weights_path=str(checkpoint),
        tile_size=32,
        num_classes=1,
        builder="resnet34-upsample",
    )
    assert loaded.is_loaded
    assert loaded.net is not road_model.net
