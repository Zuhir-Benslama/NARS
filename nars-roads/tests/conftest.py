"""Shared fixtures and helpers for the nars-roads test suite.

The auth token is set here, before any module imports app.main, so the
fail-closed auth can be exercised deterministically.
"""

import os

os.environ["NARS_ROADS_INTERNAL_TOKEN"] = "test-token"
