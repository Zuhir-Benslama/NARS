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


def test_env_int_honors_minimum(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "0")
    with pytest.raises(RuntimeError, match=">= 1"):
        env_int("NARS_ROADS_TEST_INT", 4, minimum=1)


def test_env_int_honors_maximum(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "5000")
    with pytest.raises(RuntimeError, match="<= 4096"):
        env_int("NARS_ROADS_TEST_INT", 1024, maximum=4096)


def test_env_int_accepts_bound_value(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("NARS_ROADS_TEST_INT", "1")
    assert env_int("NARS_ROADS_TEST_INT", 4, minimum=1) == 1
