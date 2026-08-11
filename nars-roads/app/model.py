"""
Multi-class segmentation model wrapper.

Single U-Net (ResNet34 encoder) producing 3 classes per pixel:
  0 = background, 1 = road, 2 = building

Training the weights is out of scope for this file - this only covers
loading a checkpoint and running inference. Swap `_build_model` for
D-LinkNet or another architecture without touching main.py or postprocess.py.
"""

from __future__ import annotations

import logging
import os
from typing import TYPE_CHECKING

import numpy as np
import rasterio
from rasterio.io import MemoryFile
from rasterio.transform import from_bounds
from rasterio.windows import Window

if TYPE_CHECKING:
    import torch

logger = logging.getLogger("nars-roads.model")

NUM_CLASSES = 3  # background, road, building
IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)

# Hard ceiling on decoded pixels (H x W) per tile. The upload-size cap bounds
# the compressed bytes, but a highly compressible TIFF can decompress to
# gigabytes, so we also bound the decoded footprint before allocating the
# output arrays. 25M pixels keeps the working set well under the pod's 4Gi
# limit while still accommodating far larger tiles than the service sees.
MAX_DECODED_PIXELS = 25_000_000


class TileTooLargeError(ValueError):
    """Raised when a tile decodes to more than MAX_DECODED_PIXELS pixels."""


def _import_torch():
    """Deferred torch import. torch is a heavy dependency and is only needed
    at inference time, so importing this module (docs, tests, health checks)
    must not force it."""
    import torch

    return torch


class SegmentationModel:
    def __init__(
        self, weights_path: str, tile_size: int = 1024, device: str | None = None
    ):
        torch = _import_torch()
        self.tile_size = tile_size
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
            classes=NUM_CLASSES,
        )

    @staticmethod
    def _normalize_window(arr: np.ndarray, dtype: np.dtype) -> np.ndarray:
        """Normalize one decoded window: (bands, H, W) -> (H, W, 3) float32
        in [0, 1]. Selects the first 3 bands (repeating band 0 for
        single-band rasters) and scales by the source bit depth so a 16-bit
        GeoTIFF lands in the same range as a uint8 one."""
        if arr.shape[0] >= 3:
            arr = arr[:3]
        else:
            arr = np.repeat(arr[:1], 3, axis=0)

        img = np.transpose(arr, (1, 2, 0))
        if np.issubdtype(dtype, np.floating):
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
                if mx > 2.0:
                    img = img / 255.0
                img = np.clip(img, 0.0, 1.0)
        else:
            # Scale by the integer bit depth: uint8 -> 255, uint16 -> 65535.
            img = img.astype(np.float32) / float(np.iinfo(dtype).max)
        return img

    def _preprocess(self, img: np.ndarray) -> torch.Tensor:
        torch = _import_torch()
        normed = (img - IMAGENET_MEAN) / IMAGENET_STD
        tensor = torch.from_numpy(normed.transpose(2, 0, 1)).float()
        return tensor.unsqueeze(0).to(self.device)

    def _predict_tile(self, img: np.ndarray) -> np.ndarray:
        """Run the net on one tile, resizing to the model's expected input
        size and back, returning per-class probabilities (H, W, NUM_CLASSES)."""
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

    def predict(
        self, raw_bytes: bytes, bbox: tuple[float, float, float, float]
    ) -> tuple[np.ndarray, np.ndarray, rasterio.Affine]:
        """Returns (road_prob, building_prob, transform) where each prob map
        is a float32 array of shape (H, W) with values in [0, 1].

        The raster is decoded one tile-sized window at a time instead of
        materializing the full image, so peak memory stays bounded by the
        window plus the prob maps. The decoded-pixel budget is enforced
        before the output arrays are allocated.

        If the file is already georeferenced (GeoTIFF) we trust that
        transform; otherwise we build one from the supplied bbox, assuming
        EPSG:4326. For tiles larger than self.tile_size a simple
        non-overlapping grid is used. Swap in overlap-and-blend if seam
        artifacts show up in practice on real imagery."""
        with MemoryFile(raw_bytes) as memfile:
            with memfile.open() as src:
                if src.transform and src.transform != rasterio.Affine.identity():
                    transform = src.transform
                else:
                    transform = from_bounds(*bbox, width=src.width, height=src.height)

                h, w = src.height, src.width
                if h * w > MAX_DECODED_PIXELS:
                    raise TileTooLargeError(
                        f"Tile decodes to {h}x{w} pixels; limit is {MAX_DECODED_PIXELS}"
                    )

                dtype = src.dtypes[0]
                probs = np.zeros((h, w, NUM_CLASSES), dtype=np.float32)
                counts = np.zeros((h, w, 1), dtype=np.float32)

                step = self.tile_size
                for y in range(0, h, step):
                    for x0 in range(0, w, step):
                        y_end = min(y + step, h)
                        x_end = min(x0 + step, w)
                        window = Window.from_slices((y, y_end), (x0, x_end))
                        chip = self._normalize_window(src.read(window=window), dtype)
                        chip_probs = self._predict_tile(chip)
                        probs[y:y_end, x0:x_end] += chip_probs
                        counts[y:y_end, x0:x_end] += 1.0

        probs = probs / np.clip(counts, 1.0, None)
        road_prob = probs[:, :, 1]
        building_prob = probs[:, :, 2]
        return road_prob, building_prob, transform
