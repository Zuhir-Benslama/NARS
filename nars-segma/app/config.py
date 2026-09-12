"""Shared configuration helpers for the segmentation service."""

import os


def env_int(
    key: str, default: int, *, minimum: int | None = None, maximum: int | None = None
) -> int:
    """Parse an integer environment variable with a clear startup error.

    Optional `minimum`/`maximum` bounds (inclusive) reject a mis-set env var
    that would otherwise silently break inference (e.g. a tile size of 0 or a
    negative timeout) at startup instead of at runtime.
    """
    raw = os.environ.get(key)
    value: int
    if raw is None:
        value = default
    else:
        try:
            value = int(raw)
        except ValueError:
            raise RuntimeError(  # noqa: TRY003 - dynamic env var name
                f"Environment variable {key} must be an integer, got: {raw!r}"
            ) from None

    if minimum is not None and value < minimum:
        raise RuntimeError(  # noqa: TRY003 - dynamic env var name
            f"Environment variable {key} must be >= {minimum}, got: {value}"
        )
    if maximum is not None and value > maximum:
        raise RuntimeError(  # noqa: TRY003 - dynamic env var name
            f"Environment variable {key} must be <= {maximum}, got: {value}"
        )

    return value
