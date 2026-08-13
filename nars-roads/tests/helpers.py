"""Shared test helpers for the nars-roads test suite.

Kept out of conftest.py so tests can import them without relying on pytest's
implicit conftest import machinery (which only works when the tests dir is on
sys.path by coincidence).
"""

import numpy as np
import pytest
from rasterio.io import MemoryFile
from rasterio.transform import Affine

try:
    import torch  # noqa: F401

    _TORCH_AVAILABLE = True
except ImportError:
    _TORCH_AVAILABLE = False

requires_torch = pytest.mark.skipif(not _TORCH_AVAILABLE, reason="torch not installed")

# Default georeferencing for generated test tiles (EPSG:4326). Keep a named
# constant so tests can assert on the exact transform instead of duplicating
# the literal.
DEFAULT_TRANSFORM = Affine(1.0, 0.0, 100.0, 0.0, -1.0, 50.0)


def make_tiff_bytes(
    width: int = 64,
    height: int = 48,
    transform: Affine | None = None,
    dtype: str = "uint8",
) -> bytes:
    """Encode a small 3-band GeoTIFF into memory. Band 1 contains a
    horizontal strip of high values so callers have something to segment."""
    dt = np.dtype(dtype)
    data = np.zeros((3, height, width), dtype=dt)
    data[1, height // 4 : height // 2, width // 8 : 7 * width // 8] = (
        np.iinfo(dt).max if dt.kind == "u" else 200.0
    )
    transform = transform or DEFAULT_TRANSFORM
    with MemoryFile() as memfile:
        with memfile.open(
            driver="GTiff",
            width=width,
            height=height,
            count=3,
            dtype=dtype,
            transform=transform,
            crs="EPSG:4326",
        ) as dst:
            dst.write(data)
        return memfile.read()
