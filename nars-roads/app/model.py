"""
Multi-class segmentation model wrapper.

Single U-Net (ResNet34 encoder) producing 3 classes per pixel:
  0 = background, 1 = road, 2 = building

Training the weights is out of scope for this file - this only covers
loading a checkpoint and running inference. Swap `_build_model` for
D-LinkNet or another architecture without touching main.py or postprocess.py.
"""

import logging
import os

import numpy as np
import rasterio
import segmentation_models_pytorch as smp
import torch
import torch.nn.functional as F
from rasterio.io import MemoryFile
from rasterio.transform import from_bounds

logger = logging.getLogger("nars-roads.model")

NUM_CLASSES = 3  # background, road, building
IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)


class SegmentationModel:
    def __init__(
        self, weights_path: str, tile_size: int = 1024, device: str | None = None
    ):
        self.tile_size = tile_size
        self.device = torch.device(
            device or ("cuda" if torch.cuda.is_available() else "cpu")
        )
        self.is_loaded = False
        self.net = self._build_model()

        if os.path.exists(weights_path):
            state_dict = torch.load(weights_path, map_location=self.device)
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
        return smp.Unet(
            encoder_name="resnet34",
            encoder_weights=None,  # real weights loaded from checkpoint above
            in_channels=3,
            classes=NUM_CLASSES,
        )

    def _read_image(self, raw_bytes: bytes, bbox: tuple[float, float, float, float]):
        """Read raw bytes into an (H, W, 3) uint8 array plus an affine transform.
        If the file is already georeferenced (GeoTIFF) we trust that transform;
        otherwise we build one from the supplied bbox, assuming EPSG:4326."""
        with MemoryFile(raw_bytes) as memfile:
            with memfile.open() as src:
                arr = src.read()  # (bands, H, W)
                if src.transform and src.transform != rasterio.Affine.identity():
                    transform = src.transform
                else:
                    transform = from_bounds(*bbox, width=src.width, height=src.height)

        if arr.shape[0] >= 3:
            arr = arr[:3]
        else:
            arr = np.repeat(arr[:1], 3, axis=0)

        img = np.transpose(arr, (1, 2, 0)).astype(np.float32)
        if img.max() > 1.0:
            img = img / 255.0
        return img, transform

    def _preprocess(self, img: np.ndarray) -> torch.Tensor:
        normed = (img - IMAGENET_MEAN) / IMAGENET_STD
        tensor = torch.from_numpy(normed.transpose(2, 0, 1)).float()
        return tensor.unsqueeze(0).to(self.device)

    @torch.no_grad()
    def _predict_tile(self, img: np.ndarray) -> np.ndarray:
        """Run the net on one tile, resizing to the model's expected input
        size and back, returning per-class probabilities (H, W, NUM_CLASSES)."""
        h, w = img.shape[:2]
        x = self._preprocess(img)
        if (h, w) != (self.tile_size, self.tile_size):
            x = F.interpolate(
                x,
                size=(self.tile_size, self.tile_size),
                mode="bilinear",
                align_corners=False,
            )

        logits = self.net(x)
        probs = F.softmax(logits, dim=1)

        if (h, w) != (self.tile_size, self.tile_size):
            probs = F.interpolate(
                probs, size=(h, w), mode="bilinear", align_corners=False
            )

        return probs.squeeze(0).permute(1, 2, 0).cpu().numpy()

    def predict(self, raw_bytes: bytes, bbox: tuple[float, float, float, float]):
        """Returns (road_prob, building_prob, transform) where each prob map
        is a float32 array of shape (H, W) with values in [0, 1].

        For tiles larger than self.tile_size, a simple non-overlapping grid
        is used. Swap in overlap-and-blend if seam artifacts show up in
        practice on real imagery."""
        img, transform = self._read_image(raw_bytes, bbox)
        h, w = img.shape[:2]

        probs = np.zeros((h, w, NUM_CLASSES), dtype=np.float32)
        counts = np.zeros((h, w, 1), dtype=np.float32)

        step = self.tile_size
        for y in range(0, h, step):
            for x0 in range(0, w, step):
                y_end = min(y + step, h)
                x_end = min(x0 + step, w)
                chip = img[y:y_end, x0:x_end]
                chip_probs = self._predict_tile(chip)
                probs[y:y_end, x0:x_end] += chip_probs
                counts[y:y_end, x0:x_end] += 1.0

        probs = probs / np.clip(counts, 1.0, None)
        road_prob = probs[:, :, 1]
        building_prob = probs[:, :, 2]
        return road_prob, building_prob, transform
