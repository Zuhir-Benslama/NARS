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


def env_float(
    key: str,
    default: float,
    *,
    minimum: float | None = None,
    maximum: float | None = None,
) -> float:
    """Parse a float environment variable with a clear startup error.

    Mirrors `env_int`: a mis-set value is rejected at startup so a bad rule
    threshold (e.g. a negative minimum road length) cannot silently change
    inference output in production.
    """
    raw = os.environ.get(key)
    value: float
    if raw is None:
        value = default
    else:
        try:
            value = float(raw)
        except ValueError:
            raise RuntimeError(  # noqa: TRY003 - dynamic env var name
                f"Environment variable {key} must be a number, got: {raw!r}"
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
