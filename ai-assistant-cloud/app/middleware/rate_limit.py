"""Per-caller request limits.

Implemented in-process with a fixed window rather than with Redis, because a single Cloud Run
instance is the deployment this service describes and adding a shared store would mean another
resource, another failure mode, and another secret. The consequence is stated plainly: with
several instances each holds its own counters, so the effective limit is the configured limit
times the instance count. That is a deliberate trade for a competition deployment, and the note
is here so it is not mistaken for a guarantee.
"""

from __future__ import annotations

import logging
import time
from collections import defaultdict
from dataclasses import dataclass
import threading

from fastapi import HTTPException, status

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class _Window:
    """One caller's counters and when the window ends."""

    counts: dict[str, int]
    resets_at: float


class RateLimiter:
    """Counts requests per caller per route family and refuses the ones over the limit."""

    def __init__(self, window_seconds: int = 60) -> None:
        self._window_seconds = window_seconds
        self._windows: dict[str, _Window] = {}
        self._lock = threading.Lock()

    def check(self, caller: str, family: str, limit: int) -> None:
        """Records a request and raises 429 when the caller is over the limit.

        The refusal is raised rather than returned so that a route cannot call this and forget to
        look at the answer. The message names the limit and when it resets, because a bare 429
        tells a person nothing they can act on.
        """

        if limit <= 0:
            return

        now = time.monotonic()
        key = f"{caller}:{family}"

        with self._lock:
            self._evict_expired(now)

            window = self._windows.get(key)

            if window is None:
                window = _Window(
                    counts=defaultdict(int),
                    resets_at=now + self._window_seconds,
                )
                self._windows[key] = window

            used = window.counts[family]
            window.counts[family] = used + 1

            reset_in = max(0, int(window.resets_at - now))

        if used >= limit:
            logger.info(
                "Rate limit reached for the %s family (caller=%s).", family, caller
            )
            raise HTTPException(
                status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                detail=(
                    f"Too many requests. The limit is {limit} per minute for this endpoint; "
                    f"try again in {reset_in} seconds."
                ),
                headers={"Retry-After": str(reset_in)},
            )

    def remaining(self, caller: str, family: str, limit: int) -> int:
        """How many requests the caller has left in this window."""

        with self._lock:
            window = self._windows.get(f"{caller}:{family}")

            if window is None:
                return limit

            return max(0, limit - window.counts[family])

    def reset(self) -> None:
        """Clears every counter, for a test that needs a clean window."""

        with self._lock:
            self._windows.clear()

    def _evict_expired(self, now: float) -> None:
        """Drops windows that have elapsed.

        Without this the map would grow one entry per caller per family for the life of the
        process, which on a long-running instance is a slow leak driven by traffic.
        """

        expired = [key for key, window in self._windows.items() if window.resets_at <= now]

        for key in expired:
            del self._windows[key]


# One limiter for the process. Module level so that a route and a test see the same counters.
rate_limiter = RateLimiter()
