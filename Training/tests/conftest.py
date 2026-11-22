"""Test tiers: `pytest -m "not slow"` = fast tier (CI, CPU); `slow` = full-event simulations with the shipped
brains (local, ~15 min)."""

import pytest


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line("markers", "slow: full-event simulations with the shipped brains (local tier, not in CI)")
