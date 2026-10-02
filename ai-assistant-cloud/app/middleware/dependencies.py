"""The authentication and rate-limit dependencies every route uses.

Kept in one place so that a new route cannot be added without them. A route that imports nothing
from here is a route that is open, and the test that checks the route table would have to know
about every route individually to catch it; this way it does not.
"""

from __future__ import annotations

import logging

from fastapi import Depends, HTTPException, Request, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.config.settings import Settings, get_settings
from app.middleware.rate_limit import rate_limiter
from app.middleware.security import AuthError, TokenService

logger = logging.getLogger(__name__)

bearer_scheme = HTTPBearer(auto_error=False)


def get_token_service(request: Request) -> TokenService:
    """The token service, reading the settings the application was built with."""

    return TokenService(settings_of(request))


def settings_of(request: Request) -> Settings:
    """The settings the application was built with.

    Read from the application state rather than from the module-level cache. The two are the same
    object in a deployed process, but not in a test that builds an application with different
    settings, and an auth decision made against a settings object the application was not built
    with is a decision about the wrong deployment.
    """

    configured = getattr(request.app.state, "settings", None)

    return configured if configured is not None else get_settings()


async def require_caller(
    request: Request,
    credentials: HTTPAuthorizationCredentials | None = Depends(bearer_scheme),
    tokens: TokenService = Depends(get_token_service),
) -> str:
    """Validates the caller's bearer token and returns who they are.

    Authentication can be turned off, and when it is the caller is named ``anonymous`` rather
    than being refused. The health check reports that state, because a deployment with
    authentication off should be visible from outside rather than discovered by reading the
    configuration.
    """

    if not tokens.settings.require_authentication:
        request.state.caller = "anonymous"
        return "anonymous"

    if credentials is None or not credentials.credentials:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="This endpoint needs a bearer token.",
            headers={"WWW-Authenticate": "Bearer"},
        )

    try:
        claims = tokens.validate(credentials.credentials)
    except AuthError as error:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail=error.args[0],
            headers={"WWW-Authenticate": "Bearer"},
        ) from error

    caller = tokens.subject_of(claims)
    request.state.caller = caller

    return caller


def limit_requests(family: str, limit_setting: str):
    """Builds a dependency that enforces one route family's rate limit.

    The limit is read from settings rather than passed as a number so that the limits live in one
    place, and so that a route cannot quietly set its own.
    """

    async def dependency(
        request: Request,
        caller: str = Depends(require_caller),
    ) -> str:
        settings = settings_of(request)
        limit = int(getattr(settings, limit_setting))
        rate_limiter.check(caller, family, limit)

        return caller

    dependency.__name__ = f"limit_{family}"

    return dependency
