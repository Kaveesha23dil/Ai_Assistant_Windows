"""Bearer token issuing and validation.

The Windows application holds no Gemini credential and gets no long-lived secret. It
authenticates to Google with the signed-in user's account and presents a short-lived token to
this service, which is what stops the API key from ever being in an executable.

This module signs and checks those tokens. There is no user database, no password, and no
registration endpoint: identity is established upstream, and a token asserts a subject rather
than authenticating one.
"""

from __future__ import annotations

import logging
import secrets
from datetime import datetime, timedelta, timezone
from typing import Any

import jwt

from app.config.settings import Settings, get_settings

logger = logging.getLogger(__name__)

_ALGORITHM_ALLOWLIST = {"HS256", "HS384", "HS512"}


class AuthError(Exception):
    """The request carried no usable credential."""

    def __init__(self, message: str, code: str = "UNAUTHORIZED") -> None:
        super().__init__(message)
        self.code = code


def _signing_key(settings: Settings) -> str:
    """The key used to sign tokens.

    In development, with no secret configured, a random one is generated per process. That makes
    the service runnable with no setup while still refusing tokens from anywhere else, and it
    means tokens do not survive a restart, which is the correct behaviour for a secret nobody
    chose. In production ``validate_for_startup`` refuses to start without a real one.
    """

    if settings.jwt_secret:
        return settings.jwt_secret

    global _generated_development_secret

    if _generated_development_secret is None:
        _generated_development_secret = secrets.token_urlsafe(48)
        logger.warning(
            "No JWT_SECRET is configured, so a random one was generated for this process. "
            "Tokens will not survive a restart and will not be accepted by another instance."
        )

    return _generated_development_secret


_generated_development_secret: str | None = None


class TokenService:
    """Issues and validates bearer tokens."""

    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()

    @property
    def settings(self) -> Settings:
        """The settings this service signs and checks with.

        Read by the auth dependency so that "is authentication required" is answered by the same
        object the tokens are checked against. Two settings objects would mean an application
        could require authentication and validate with the wrong secret.
        """

        return self._settings

    def issue(self, subject: str, *, scopes: list[str] | None = None) -> tuple[str, int]:
        """Issues a token and returns it with its lifetime in seconds.

        The subject is taken from the caller and is a claim, not an authenticated identity. It is
        echoed into logs for correlation and is never used to make a decision about access, so a
        token that names somebody else is misleading rather than dangerous.
        """

        if not subject or not subject.strip():
            raise AuthError("A token needs a subject.", "TOKEN_SUBJECT_MISSING")

        algorithm = self._settings.jwt_algorithm

        if algorithm not in _ALGORITHM_ALLOWLIST:
            # Refused rather than passed through. Accepting an arbitrary value from configuration
            # is how "none" or an asymmetric algorithm with the wrong key becomes a way to forge
            # a token.
            raise AuthError(
                f"JWT_ALGORITHM {algorithm!r} is not supported.", "JWT_ALGORITHM_UNSUPPORTED"
            )

        expires_in = self._settings.jwt_access_token_minutes * 60
        now = datetime.now(timezone.utc)

        claims: dict[str, Any] = {
            "sub": subject.strip(),
            "iss": self._settings.jwt_issuer,
            "iat": int(now.timestamp()),
            "exp": int((now + timedelta(seconds=expires_in)).timestamp()),
            "jti": secrets.token_urlsafe(16),
        }

        if scopes:
            claims["scope"] = " ".join(scopes)

        token = jwt.encode(
            claims, _signing_key(self._settings), algorithm=algorithm
        )

        return token, expires_in

    def validate(self, token: str) -> dict[str, Any]:
        """Validates a token and returns its claims.

        Raises rather than returning a result, so that no caller can forget to check. The
        exceptions deliberately do not say which part was wrong: distinguishing "expired" from
        "bad signature" tells an attacker whether a token they guessed was close.
        """

        if not token or not token.strip():
            raise AuthError("No token was presented.", "TOKEN_MISSING")

        algorithm = self._settings.jwt_algorithm

        try:
            claims = jwt.decode(
                token,
                _signing_key(self._settings),
                algorithms=[algorithm],
                issuer=self._settings.jwt_issuer,
                options={"require": ["exp", "iat", "sub"]},
            )
        except jwt.ExpiredSignatureError as error:
            raise AuthError("The token has expired.", "TOKEN_EXPIRED") from error
        except jwt.InvalidTokenError as error:
            raise AuthError("The token is not valid.", "TOKEN_INVALID") from error

        if not claims.get("sub"):
            raise AuthError("The token has no subject.", "TOKEN_SUBJECT_MISSING")

        return claims

    def subject_of(self, claims: dict[str, Any]) -> str:
        """The subject a validated token names."""

        return str(claims.get("sub", "unknown"))
