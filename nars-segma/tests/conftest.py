"""Shared fixtures and helpers for the nars-segma test suite.

The auth token is set here, before any module imports app.main, so the
fail-closed auth can be exercised deterministically.
"""

import os

from helpers import AUTH_TOKEN

os.environ["NARS_SEGMA_INTERNAL_TOKEN"] = AUTH_TOKEN
# The unit suite builds SegmentationModel fixtures on the CPU; production
# enforcement (NARS_SEGMA_REQUIRE_CUDA default 1, fail-closed) is what the
# dedicated CudaUnavailableError test asserts separately.
os.environ["NARS_SEGMA_REQUIRE_CUDA"] = "0"
