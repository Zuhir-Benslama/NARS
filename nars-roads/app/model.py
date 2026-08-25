"""
Single-class segmentation model wrapper (background / foreground).

One U-Net (ResNet34 encoder) produces a 2-class-per-pixel map:
  0 = background, 1 = the foreground object (building, road, ...)

Each feature type gets its own `SegmentationModel` instance (see the model
registry in main.py), so checkpoints can be swapped and released
independently. The buildings checkpoint (`unet_bldg_base.pth`) is the HOT
fAIr building baseline from
https://huggingface.co/nilsho01/unet-resnet34-vhr-buildings, a drop-in
smp.Unet(resnet34, classes=2) state_dict. Training the weights is out of
scope for this file - this only covers loading a checkpoint and running
inference. Swap `_build_model` for D-LinkNet or another architecture
without touching main.py or postprocess.py.
"""

from __future__ import annotations

import logging
import os
from typing import TYPE_CHECKING

import numpy as np
import rasterio
from rasterio.errors import RasterioIOError
from rasterio.io import MemoryFile
from rasterio.transform import from_bounds
from rasterio.windows import Window

if TYPE_CHECKING:
    import torch

__all__ = [
    "SegmentationModel",
    "InvalidTileError",
    "TileTooLargeError",
]

logger = logging.getLogger("nars-roads.model")

IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)

# Threshold for detecting byte-scaled float input. Values in (1.0, this
# threshold] are treated as sensor noise on a [0,1] raster and clipped;
# values above this threshold are assumed to be [0,255] and rescaled.
FLOAT_BYTE_SCALE_THRESHOLD = 2.0

# Hard ceiling on decoded pixels (H x W) per tile. The upload-size cap bounds
# the compressed bytes, but a highly compressible TIFF can decompress to
# gigabytes, so we also bound the decoded footprint before allocating the
# output arrays. 25M pixels keeps the working set well under the pod's 4Gi
# limit while still accommodating far larger tiles than the service sees.
def _env_int(key: str, default: int) -> int:
    """Parse an integer environment variable with a clear startup error."""
    raw = os.environ.get(key)
    if raw is None:
        return default
    try:
        return int(raw)
    except ValueError:
        raise RuntimeError(  # noqa: TRY003 - dynamic env var name
            f"Environment variable {key} must be an integer, got: {raw!r}"
        ) from None


MAX_DECODED_PIXELS = _env_int("NARS_ROADS_MAX_DECODED_PIXELS", 25_000_000)


class TileTooLargeError(ValueError):
    """Raised when a tile decodes to more than MAX_DECODED_PIXELS pixels."""


class InvalidTileError(ValueError):
    """Raised when the uploaded bytes cannot be decoded as a readable image."""


def _import_torch():
    """Deferred torch import. torch is a heavy dependency and is only needed
    at inference time, so importing this module (docs, tests, health checks)
    must not force it."""
    import torch

    return torch


class SegmentationModel:
    def __init__(
        self,
        weights_path: str,
        tile_size: int = 1024,
        num_classes: int = 2,
        device: str | None = None,
    ):
        torch = _import_torch()
        self.tile_size = tile_size
        self.num_classes = num_classes
        self.device = torch.device(
            device or ("cuda" if torch.cuda.is_available() else "cpu")
        )
        self.is_loaded = False
        self.net = self._build_model()

        if os.path.isfile(weights_path):
            # weights_only=True rejects the pickle gadgets that allow
            # arbitrary code execution from a malicious checkpoint. Our
            # checkpoints are pure state_dicts, so this is always safe.
            state_dict = torch.load(
                weights_path, map_location=self.device, weights_only=True
            )
            self.net.load_state_dict(state_dict)
            self.is_loaded = True
            logger.info("Loaded weights from %s", weights_path)
        else:
            logger.warning(
                "Weights file not found at %s - serving with randomly "
                "initialized weights. Predictions will be meaningless "
                "until real weights are mounted.",
                weights_path,
            )

        self.net.to(self.device)
        self.net.eval()

    def _build_model(self) -> torch.nn.Module:
        import segmentation_models_pytorch as smp

        return smp.Unet(
            encoder_name="resnet34",
            encoder_weights=None,  # real weights loaded from checkpoint above
            in_channels=3,
            classes=self.num_classes,
        )

    @staticmethod
    def _normalize_window(arr: np.ndarray) -> np.ndarray:
        """Normalize one decoded window: (bands, H, W) -> (H, W, 3) float32
        in [0, 1]. Selects the first 3 bands (repeating band 0 for
        single-band rasters) and scales by the source bit depth so a 16-bit
        GeoTIFF lands in the same range as a uint8 one. The scale is derived
        from the array's own dtype — rasterio decodes every band into the
        dataset's band-0 dtype, so that is the only scale the values actually
        carry."""
        arr = arr[:3] if arr.shape[0] >= 3 else np.repeat(arr[:1], 3, axis=0)

        img = np.transpose(arr, (1, 2, 0))
        if np.issubdtype(arr.dtype, np.floating):
            # Float input is assumed already normalized to [0, 1]; some
            # producers ship float data in [0, 255]. Rescale only when it is
            # clearly byte-scaled (max well above 1): a value like 1.02 is
            # sensor noise on a [0,1] raster and must not be divided by 255
            # (which would black it). Non-finite values are neutralized so a
            # single NaN/Inf can't poison normalization downstream.
            img = img.astype(np.float32)
            img = np.nan_to_num(img, nan=0.0, posinf=1.0, neginf=0.0)
            mx = float(img.max())
            if mx > 1.0:
                if mx > FLOAT_BYTE_SCALE_THRESHOLD:
                    img = img / 255.0
                img = np.clip(img, 0.0, 1.0)
        else:
            # Scale by the integer bit depth: uint8 -> 255, uint16 -> 65535.
            img = img.astype(np.float32) / float(np.iinfo(arr.dtype).max)
        return img

    def _preprocess(self, img: np.ndarray) -> torch.Tensor:
        torch = _import_torch()
        normed = (img - IMAGENET_MEAN) / IMAGENET_STD
        tensor = torch.from_numpy(normed.transpose(2, 0, 1)).float()
        return tensor.unsqueeze(0).to(self.device)

    def _predict_tile(self, img: np.ndarray) -> np.ndarray:
        """Run the net on one tile, resizing to the model's expected input
        size and back, returning per-class probabilities
        (H, W, self.num_classes) as float32."""
        torch = _import_torch()
        import torch.nn.functional as F

        h, w = img.shape[:2]
        x = self._preprocess(img)
        if (h, w) != (self.tile_size, self.tile_size):
            x = F.interpolate(
                x,
                size=(self.tile_size, self.tile_size),
                mode="bilinear",
                align_corners=False,
            )

        with torch.no_grad():
            logits = self.net(x)
            probs = F.softmax(logits, dim=1)

        if (h, w) != (self.tile_size, self.tile_size):
            probs = F.interpolate(
                probs, size=(h, w), mode="bilinear", align_corners=False
            )

        return probs.squeeze(0).permute(1, 2, 0).cpu().numpy()

    @staticmethod
    def _embedded_transform(src) -> rasterio.Affine | None:
        """The tile's own georeferencing, but only when it can be trusted to
        describe geographic (degree-unit) coordinates.

        The service's contract is EPSG:4326 GeoJSON out, so a projected CRS
        (e.g. UTM, metres) would silently emit meter-scale coordinates as if
        they were degrees — a garbage result with no error. A transform with
        no CRS is equally unverifiable. Both fall back to the caller-supplied
        bbox (the untrusted, likely-arbitrary bbox is exactly what the
        fallback path is for), which is always required and always correct
        for the /segment contract."""
        transform = src.transform
        # src.transform is never None (rasterio falls back to identity when
        # the file carries no georeferencing), so only equality can detect
        # "no usable transform".
        if transform == rasterio.Affine.identity():
            return None
        if src.crs is None or not src.crs.is_geographic:
            return None
        return transform

    def predict(
        self, raw_bytes: bytes, bbox: tuple[float, float, float, float]
    ) -> tuple[np.ndarray, rasterio.Affine]:
        """Returns (fg_prob, transform) where fg_prob is the foreground-class
        (channel 1) probability map, a float32 array of shape (H, W) with
        values in [0, 1].

        The raster is decoded one tile-sized window at a time instead of
        materializing the full image, so peak memory stays bounded by the
        window plus the prob maps. The decoded-pixel budget is enforced
        before the output arrays are allocated.

        The tile's own transform is used only when it can be trusted to
        describe an EPSG:4326-style geographic raster (see
        `_embedded_transform`); otherwise one is built from the supplied
        bbox, assuming EPSG:4326. For tiles larger than self.tile_size a
        simple non-overlapping grid is used. Swap in overlap-and-blend if
        seam artifacts show up in practice on real imagery."""
        with MemoryFile(raw_bytes) as memfile:
            try:
                with memfile.open() as src:
                    transform = self._embedded_transform(src) or from_bounds(
                        *bbox, width=src.width, height=src.height
                    )

                    h, w = src.height, src.width
                    if h * w > MAX_DECODED_PIXELS:
                        raise TileTooLargeError(  # noqa: TRY003 - dynamic message
                            f"Tile decodes to {h}x{w} pixels; "
                            f"limit is {MAX_DECODED_PIXELS}"
                        )

                    probs = np.zeros((h, w, self.num_classes), dtype=np.float32)
                    counts = np.zeros((h, w, 1), dtype=np.float32)

                    step = self.tile_size
                    for y in range(0, h, step):
                        for x0 in range(0, w, step):
                            y_end = min(y + step, h)
                            x_end = min(x0 + step, w)
                            window = Window.from_slices((y, y_end), (x0, x_end))
                            chip = self._normalize_window(src.read(window=window))
                            chip_probs = self._predict_tile(chip)
                            probs[y:y_end, x0:x_end] += chip_probs
                            counts[y:y_end, x0:x_end] += 1.0
            except RasterioIOError as exc:
                # Decoding a garbage/truncated upload raises here; surface it
                # as a 4xx client error instead of a 500.
                raise InvalidTileError(  # noqa: TRY003 - dynamic message
                    f"Tile could not be decoded: {exc}"
                ) from exc

        probs = probs / np.clip(counts, 1.0, None)
        fg_prob = probs[:, :, 1]
        return fg_prob, transform
