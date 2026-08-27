"""Shared configuration helpers for the segmentation service."""

import os


def env_int(key: str, default: int) -> int:
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
