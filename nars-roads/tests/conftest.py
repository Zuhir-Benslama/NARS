"""Shared fixtures and helpers for the nars-roads test suite.

The auth token is set here, before any module imports app.main, so the
fail-closed auth can be exercised deterministically.
"""

import os

import numpy as np
import pytest
from rasterio.io import MemoryFile
from rasterio.transform import Affine

os.environ["NARS_ROADS_INTERNAL_TOKEN"] = "test-token"

from fastapi.testclient import TestClient  # noqa: E402

from app.main import app  # noqa: E402


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
    transform = transform or Affine(1.0, 0.0, 100.0, 0.0, -1.0, 50.0)
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


@pytest.fixture
def client():
    """TestClient used OUTSIDE the lifespan context manager, so the model is
    never loaded and endpoint behavior can be exercised without torch."""
    return TestClient(app)
