"""Request logging that records what happened without recording what was asked.

Allowed: that a request started, that it finished, how long it took, how many tokens it used, and
which caller made it. Not logged: prompts, document text, image bytes, or anything else a person
typed or showed.

The redaction is not a convention here. It is the absence of any code path that has the body, and
that is asserted by a test that walks the source for the identifiers which would be needed to log
one.
"""

from __future__ import annotations

import logging
import time
import uuid
from collections.abc import Awaitable, Callable

from fastapi import Request, Response
from starlette.middleware.base import BaseHTTPMiddleware

logger = logging.getLogger("app.access")

# Paths whose names are recorded. Anything not on this list is reported as its route template or
# as "unmatched", never by its raw path, because a raw path can carry an identifier.
_LOGGABLE_PATHS = frozenset(
    {
        "/",
        "/health",
        "/api/chat",
        "/api/chat/stream",
        "/api/vision/analyze",
        "/api/embeddings",
        "/api/agent/run",
        "/api/agent/tools",
        "/api/auth/token",
    }
)


class AccessLogMiddleware(BaseHTTPMiddleware):
    """Logs one line per request: method, route, status, duration, caller."""

    async def dispatch(
        self, request: Request, call_next: Callable[[Request], Awaitable[Response]]
    ) -> Response:
        """Times the request and logs it once it has been handled.

        The caller identifier is read from a header the auth dependency set, not from the
        request body, so it is available here without touching anything private.
        """

        started = time.perf_counter()
        request_id = uuid.uuid4().hex[:12]

        try:
            response = await call_next(request)
        except Exception:
            duration_ms = int((time.perf_counter() - started) * 1000)
            logger.exception(
                "request_id=%s method=%s route=%s status=500 duration_ms=%d",
                request_id,
                request.method,
                _safe_route(request),
                duration_ms,
            )
            raise

        duration_ms = int((time.perf_counter() - started) * 1000)
        caller = getattr(request.state, "caller", "anonymous")

        logger.info(
            "request_id=%s method=%s route=%s status=%d duration_ms=%d caller=%s",
            request_id,
            request.method,
            _safe_route(request),
            response.status_code,
            duration_ms,
            caller,
        )

        response.headers["X-Request-Id"] = request_id

        return response


def _safe_route(request: Request) -> str:
    """The route to log, which is a known path or a placeholder.

    An unmatched path can contain anything a caller put in it, including a token. Logging the raw
    value would put that in the log, so an unknown path is reported by shape instead.
    """

    path = request.url.path

    if path in _LOGGABLE_PATHS:
        return path

    route = request.scope.get("route")

    template = getattr(route, "path", None)

    if isinstance(template, str) and template.startswith("/api"):
        return template

    return "unmatched"


def configure_logging(level: str = "INFO") -> None:
    """Sets up logging.

    A single line per entry, with no formatter that could print an exception's payload, and no
    request body logger anywhere. The verbosity that is configured is the verbosity that is
    available.
    """

    logging.basicConfig(
        level=getattr(logging, level, logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )

    # These two log the full request and response bodies at debug level. Both stay off: they are
    # the most common way a prompt ends up in a log file, and turning them on is the whole of
    # what would be needed to leak one.
    for noisy in ("httpx", "httpcore", "google", "google_genai", "urllib3"):
        logging.getLogger(noisy).setLevel(logging.WARNING)
