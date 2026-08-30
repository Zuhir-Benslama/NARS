"""Tests for app/config.py — env parsing shared across the service."""

import pytest

from app.config import env_int


def test_env_int_returns_default_when_unset(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("NARS_ROADS_TEST_INT", raising=False)
    assert env_int("NARS_ROADS_TEST_INT", 42) == 42


def test_env_int_parses_integer(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "7")
    assert env_int("NARS_ROADS_TEST_INT", 42) == 7


def test_env_int_parses_negative(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "-3")
    assert env_int("NARS_ROADS_TEST_INT", 42) == -3


def test_env_int_rejects_non_integer(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "not-a-number")
    with pytest.raises(RuntimeError, match="not-a-number"):
        env_int("NARS_ROADS_TEST_INT", 42)
