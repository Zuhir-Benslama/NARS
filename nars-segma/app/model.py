"""
Single-class segmentation model wrapper (background / foreground).

One U-Net (ResNet34 encoder) produces a 2-class-per-pixel map:
  0 = background, 1 = the foreground object (building, road, ...)

Each feature type gets its own `SegmentationModel` instance (see the model
registry in main.py), so checkpoints can be swapped and released
independently. The buildings checkpoint (`unet_bldg_base.pth`) is the HOT
fAIr building baseline from
https://huggingface.co/nilsho01/unet-resnet34-vhr-buildings, a drop-in
smp.Unet(resnet34, classes=2) state_dict. The roads checkpoint is the
SpaceNet 3 champion (Resnet34Upsample from app/road_model.py, a single-logit
sigmoid model). Training the weights is out of scope for this file - this
only covers loading a checkpoint and running inference. Swap `builder` for
D-LinkNet or another architecture without touching main.py or postprocess.py.
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

from app.config import env_int

if TYPE_CHECKING:
    import torch

__all__ = [
    "CudaUnavailableError",
    "InvalidTileError",
    "SegmentationModel",
    "TileTooLargeError",
]

logger = logging.getLogger("nars-segma.model")

IMAGENET_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)

# Threshold for detecting byte-scaled float input. Values in (1.0, this
# threshold] are treated as sensor noise on a [0,1] raster and clipped;
# values above this threshold are assumed to be [0,255] and rescaled.
FLOAT_BYTE_SCALE_THRESHOLD = 2.0

# nars-segma is CUDA-only: the deployment requests an nvidia.com/gpu and the
# models fail closed without a CUDA device (see SegmentationModel.__init__).
# Set to 0 only for CPU-only tooling/tests (e.g. the unit-test suite).
REQUIRE_CUDA = os.environ.get("NARS_SEGMA_REQUIRE_CUDA", "1").strip().lower() not in (
    "0",
    "false",
    "no",
    "",
)


class CudaUnavailableError(RuntimeError):
    """Raised when REQUIRE_CUDA is set but torch cannot see a CUDA device."""

    def __init__(self) -> None:
        super().__init__(
            "CUDA is required but torch.cuda.is_available() is False; "
            "nars-segma is CUDA-only. Schedule the pod on a GPU node, or "
            "set NARS_SEGMA_REQUIRE_CUDA=0 for CPU-only tooling/tests."
        )


# Hard ceiling on decoded pixels (H x W) per tile. The upload-size cap bounds
# the compressed bytes, but a highly compressible TIFF can decompress to
# gigabytes, so we also bound the decoded footprint before allocating the
# output arrays. 64M pixels (an 8000x8000 tile) keeps the working set within
# the pod's 4Gi limit and accommodates a 19x19 z18 grid (~23.6M px) — the
# largest input the road pipeline produces. (A 2x upscale test raised decoded
# pixels to 94M with NO recall gain, so the cap stays at 64M.)
MAX_DECODED_PIXELS = env_int(
    "NARS_SEGMA_MAX_DECODED_PIXELS", 64_000_000, minimum=1_000, maximum=1_000_000_000
)

# Overlap (pixels) between neighbouring decode windows. Neighbouring windows
# are max-merged, so a thin road crossing a seam keeps its full probability on
# both sides instead of being cut exactly where windows abut. At a 1024px tile
# a 256px overlap costs ~25% more windows — cheap on the GPU serving path.
# Capped below the tile size in predict() so small test tiles can't degenerate.
OVERLAP_PX = env_int("NARS_SEGMA_OVERLAP_PX", 256, minimum=0, maximum=1024)


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
        builder: str = "smp-unet",
    ):
        torch = _import_torch()
        self.tile_size = tile_size
        self.num_classes = num_classes
        self.builder = builder
        # Multi-class models (buildings) emit per-class logits - softmax over
        # the classes. Single-class models (roads) emit one foreground logit
        # that is passed through sigmoid instead (softmax of a single value is
        # a constant 1.0, so it would destroy the probability estimate).
        self.activation = "softmax" if num_classes > 1 else "sigmoid"
        cuda_available = bool(torch.cuda.is_available())
        if REQUIRE_CUDA and not cuda_available:
            raise CudaUnavailableError()
        self.device = torch.device(device or ("cuda" if cuda_available else "cpu"))
        # Preprocessing contract per task. The smp-resnet34 buildings model
        # (HOT fAIr) was trained with ImageNet mean/std normalization, as
        # segmentation_models_pytorch expects. The SpaceNet 3 champion roads
        # checkpoint (resnet34-upsample) was trained on plain `/255` inputs —
        # its test harness divides by 255 only, no per-channel mean/std (see
        # albu-solution/src/augmentations/functional.py img_to_tensor). Feeding
        # it ImageNet-normalized pixels statistically shifted the activations
        # far negative and the model emitted an all-background mask everywhere
        # (a real regression seen on dense urban tiles). The flag keeps the
        # correct transform per architecture.
        self.imagenet_norm = builder != "resnet34-upsample"
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
        if self.builder == "resnet34-upsample":
            from app.road_model import Resnet34Upsample

            return Resnet34Upsample(num_classes=self.num_classes, num_channels=3)

        if self.builder == "deeplabv3p-convnext":
            from app.deeplabv3p import DeepLabV3Plus

            # Serving builds pretrained=False: the trained ConvNeXt encoder is
            # carried in the exported checkpoint (see app/deeplabv3p.py), so
            # inference never downloads ImageNet weights over the network.
            return DeepLabV3Plus(num_classes=self.num_classes, pretrained=False)

        import segmentation_models_pytorch as smp

        return smp.Unet(
            encoder_name="resnet34",
            encoder_weights=None,  # real weights loaded from checkpoint above
            in_channels=3,
            classes=self.num_classes,
        )

    @staticmethod
    def _normalize_window(arr: np.ndarray, scale: float | None = None) -> np.ndarray:
        """Normalize one decoded window: (bands, H, W) -> (H, W, 3) float32
        in [0, 1]. Selects the first 3 bands (repeating band 0 for
        single-band rasters). Integer pixels are divided by `scale` when the
        caller derived one from the raster's actual data range (see
        `_integer_scale`); otherwise by the array's own bit depth — rasterio
        decodes every band into the dataset's band-0 dtype, so that is the
        only scale the values carry by default."""
        arr = arr[:3] if arr.shape[0] >= 3 else np.repeat(arr[:1], 3, axis=0)

        img = np.transpose(arr, (1, 2, 0))
        if np.issubdtype(arr.dtype, np.floating):
            # Float input is assumed already normalized to [0, 1]; some
            # producers ship float data in [0, 255]. Rescale only when it is
            # clearly byte-scaled (max well above 1): a value like 1.02 is
            # sensor noise on a [0,1] raster and must not be divided by 255
            # (which would black it). Non-finite values are neutralized so a
            # single NaN/Inf can't poison normalization downstream. Clipping
            # always runs so negative pixels (float rasters are not required
            # to be padded to zero) can't reach the net's ImageNet-based
            # normalization with unclipped values.
            img = img.astype(np.float32)
            img = np.nan_to_num(img, nan=0.0, posinf=1.0, neginf=0.0)
            if float(img.max()) > FLOAT_BYTE_SCALE_THRESHOLD:
                img = img / 255.0
            img = np.clip(img, 0.0, 1.0)
        else:
            # Integer pixels: scale by the caller-derived raster range when
            # given (see _integer_scale), else by the bit depth: uint8 -> 255,
            # uint16 -> 65535. The dtype-max fallback is only correct when the
            # data spans the full depth; a low-range 16-bit product (e.g. DNs
            # in 10-1500) would collapse to ~0 and come out black, which is
            # what the probe in _integer_scale guards against.
            if scale is None:
                scale = float(np.iinfo(arr.dtype).max)
            img = img.astype(np.float32) / scale
        return img

    @staticmethod
    def _integer_scale(src: rasterio.DatasetReader) -> float:
        """Integer normalization denominator for one raster.

        Scaling by the dtype's full depth (255/65535) is only meaningful when
        the data actually spans it; a low-range 16-bit tile (e.g. DNs in
        10-1500) would be divided by 65535, collapse to ~0 and come out black.
        Probe a decimated read for the real span once per raster: near the
        full depth, keep the dtype max (preserves absolute radiometric range
        across differently-deep datasets); otherwise min-max scale by the
        observed max so low-range data is actually visible. Float tiles
        self-normalize in `_normalize_window`, so they get an identity scale."""
        dtype = np.dtype(src.dtypes[0])
        if not np.issubdtype(dtype, np.integer):
            return 1.0
        dtype_max = float(np.iinfo(dtype).max)
        sampled = src.read(
            out_shape=(
                src.count,
                max(src.height // 16, 1),
                max(src.width // 16, 1),
            )
        )
        observed_max = float(sampled.max())
        if observed_max >= 0.5 * dtype_max:
            return dtype_max
        return max(observed_max, 1.0)

    def _preprocess(self, img: np.ndarray) -> torch.Tensor:
        torch = _import_torch()
        # See `imagenet_norm` in __init__: only architectures trained with the
        # ImageNet statistics get them; the SpaceNet roads model expects the
        # raw /255-scaled tensor and goes straight through.
        if self.imagenet_norm:
            img = (img - IMAGENET_MEAN) / IMAGENET_STD
        tensor = torch.from_numpy(img.transpose(2, 0, 1)).float()
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
            if self.activation == "sigmoid":
                probs = torch.sigmoid(logits)
            else:
                probs = F.softmax(logits, dim=1)

        if (h, w) != (self.tile_size, self.tile_size):
            probs = F.interpolate(
                probs, size=(h, w), mode="bilinear", align_corners=False
            )

        return probs.squeeze(0).permute(1, 2, 0).cpu().numpy()

    @staticmethod
    def _embedded_transform(src: rasterio.DatasetReader) -> rasterio.Affine | None:
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
        bbox, assuming EPSG:4326. For tiles larger than self.tile_size an
        overlapping grid (stride = tile_size - OVERLAP_PX) is decoded and the
        window predictions are max-merged, so a thin road that crosses a
        window seam keeps its full probability instead of being cut there."""
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

                    # Compute the integer normalization denominator once per
                    # raster so every window shares one scale (see
                    # _integer_scale): a per-window min-max would make scaling
                    # drift between tiles and create brightness seams.
                    scale = self._integer_scale(src)

                    # Walk the raster in overlapping windows and max-merge
                    # each prediction into the prob map. Each chip is still
                    # tile_size^2 (full native resolution for the network);
                    # the window ORIGINS advance by step = tile_size - overlap,
                    # so neighbouring windows re-score a shared strip. Overlap
                    # rescues thin road segments that fall exactly on a window
                    # seam (see OVERLAP_PX); max (not average) keeps a high-
                    # confidence detection intact and cannot lower one the
                    # model already fired on. The overlap is clamped below half
                    # the tile so small test tiles and degenerate configs stay
                    # sane.
                    overlap = min(OVERLAP_PX, self.tile_size // 2)
                    step = max(self.tile_size - overlap, 1)
                    for y0 in range(0, h, step):
                        y = min(y0, h - 1)
                        y_end = min(y0 + self.tile_size, h)
                        for x0 in range(0, w, step):
                            x = min(x0, w - 1)
                            x_end = min(x0 + self.tile_size, w)
                            window = Window.from_slices((y, y_end), (x, x_end))
                            chip = self._normalize_window(
                                src.read(window=window), scale=scale
                            )
                            chip_probs = self._predict_tile(chip)
                            # `max` over the overlap region (not `=`): a pixel
                            # seen by multiple windows keeps the strongest
                            # prediction instead of the last writer.
                            region = probs[y:y_end, x:x_end]
                            np.maximum(region, chip_probs, out=region)
            except RasterioIOError as exc:
                # Decoding a garbage/truncated upload raises here; surface it
                # as a 4xx client error instead of a 500.
                raise InvalidTileError(  # noqa: TRY003 - dynamic message
                    f"Tile could not be decoded: {exc}"
                ) from exc

        fg_prob = probs[:, :, self.num_classes - 1]
        return fg_prob, transform
