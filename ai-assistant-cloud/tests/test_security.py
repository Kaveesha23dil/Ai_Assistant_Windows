"""Authentication, rate limiting, and what the logs are allowed to contain.

The threat model is narrow and worth stating: this API holds a credential that can spend money, it
accepts pictures of the user's screen, and it answers from the user's own documents. So these tests
cover who may call it, how often, and what a log reader or a curious operator can see.
"""

from __future__ import annotations

import base64
import json
import time
from collections.abc import Iterator
from contextlib import contextmanager
from typing import Any

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient

from app.config.settings import Settings
from app.middleware.rate_limit import RateLimiter, rate_limiter
from app.middleware.security import AuthError, TokenService, _signing_key
from tests.fakes import FakeGeminiClient, make_gemini, make_settings
from tests.support import running_client

PNG = b"\x89PNG\r\n\x1a\n" + b"0" * 200


def png_base64() -> str:
    """A base64 PNG."""

    return base64.b64encode(PNG).decode("ascii")


# ---------------------------------------------------------------- tokens


def test_a_token_names_the_caller_and_comes_back() -> None:
    """A round trip: what is issued can be presented again."""

    tokens = TokenService(make_settings())

    token, lifetime = tokens.issue("local-developer")
    claims = tokens.validate(token)

    assert claims["sub"] == "local-developer"
    assert lifetime > 0


def test_a_token_signed_with_another_secret_is_refused() -> None:
    """A token from a different deployment is not accepted here.

    Otherwise a token read from any other instance of this service, or forged with a key lifted
    from a compromised one, would be let in.
    """

    issuer = TokenService(make_settings(jwt_secret="the-first-secret-value"))
    verifier = TokenService(make_settings(jwt_secret="an-entirely-different-secret"))

    with pytest.raises(AuthError):
        verifier.validate(issuer.issue("someone")[0])


def test_an_unsigned_token_is_refused() -> None:
    """A token claiming to be signed with nothing is refused.

    This is the "alg: none" forgery. Accepting it would make the signature check decorative.
    """

    tokens = TokenService(make_settings())

    header = {"alg": "none", "typ": "JWT"}
    body = {"sub": "attacker", "exp": int(time.time()) + 600}
    unsigned = f"{_segment(header)}.{_segment(body)}."

    with pytest.raises(AuthError):
        tokens.validate(unsigned)


def test_an_expired_token_is_refused() -> None:
    """An expired token is refused even when the signature is good.

    A token that outlived its window is a credential left in a log, and refusing it is the entire
    reason the window exists.
    """

    tokens = TokenService(make_settings())

    with pytest.raises(AuthError):
        tokens.validate(_expired_token(tokens))


def test_garbage_presented_as_a_token_is_refused() -> None:
    """Nonsense is refused, and no decoding error escapes as a 500."""

    tokens = TokenService(make_settings())

    with pytest.raises(AuthError):
        tokens.validate("this is not a token")


def test_a_token_with_no_subject_is_refused() -> None:
    """A token that names nobody cannot be issued.

    The subject is what appears in the access log next to the request, so an empty one would put
    an unattributable request in the record.
    """

    tokens = TokenService(make_settings())

    with pytest.raises(AuthError):
        tokens.issue("   ")


def test_a_configured_algorithm_outside_the_allowlist_is_refused() -> None:
    """An algorithm from configuration is not honoured without being on the list.

    Accepting whatever the configuration says is how "none", or an asymmetric algorithm verified
    with the wrong key, becomes a way to forge a token.
    """

    tokens = TokenService(make_settings(jwt_algorithm="RS256"))

    with pytest.raises(AuthError):
        tokens.issue("someone")


# ---------------------------------------------------------------- endpoints


def test_a_request_without_a_token_is_refused(authenticated: tuple[TestClient, Settings]) -> None:
    """401, with a challenge, so a client knows what to send."""

    client, _ = authenticated

    response = client.post("/api/chat", json={"message": "A question"})

    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_a_request_with_a_bad_token_is_refused(authenticated: tuple[TestClient, Settings]) -> None:
    """A malformed token is refused the same way a missing one is."""

    client, _ = authenticated

    response = client.post(
        "/api/chat",
        json={"message": "A question"},
        headers={"Authorization": "Bearer nonsense"},
    )

    assert response.status_code == 401


def test_a_request_with_a_good_token_is_accepted(authenticated: tuple[TestClient, Settings]) -> None:
    """The full path, with a token this deployment issued."""

    client, settings = authenticated
    token, _ = TokenService(settings).issue("local-developer")

    response = client.post(
        "/api/chat",
        json={"message": "A question"},
        headers={"Authorization": f"Bearer {token}"},
    )

    assert response.status_code == 200


def test_the_caller_is_named_in_the_log_for_an_authenticated_request(
    authenticated: tuple[TestClient, Settings], caplog: pytest.LogCaptureFixture
) -> None:
    """The log identifies who asked, which is the reason to log it at all."""

    client, settings = authenticated
    token, _ = TokenService(settings).issue("local-developer")

    with caplog.at_level("INFO", logger="app.access"):
        client.post(
            "/api/chat",
            json={"message": "A question"},
            headers={"Authorization": f"Bearer {token}"},
        )

    assert "local-developer" in caplog.text
    assert token not in caplog.text


def test_the_health_check_needs_no_token(authenticated: tuple[TestClient, Settings]) -> None:
    """A load balancer or uptime monitor has no credential, so health must not need one."""

    client, _ = authenticated

    assert client.get("/health").status_code == 200


def test_the_development_token_endpoint_is_gone_when_authentication_is_on(
    authenticated: tuple[TestClient, Settings],
) -> None:
    """The shortcut that mints a token for anyone is not available on a secured deployment.

    Left in place it is a way around authentication entirely: ask for a token, then use it.
    """

    client, _ = authenticated

    assert client.post("/api/auth/token", json={"subject": "anyone"}).status_code == 404


def test_the_development_token_endpoint_works_when_authentication_is_off(
    client: TestClient,
) -> None:
    """A local run with authentication off can still obtain a token, which is the only reason the
    endpoint exists: to exercise the secured routes during development."""

    response = client.post("/api/auth/token", json={"subject": "local-developer"})

    assert response.status_code == 200
    assert response.json()["accessToken"]


def test_the_development_token_endpoint_is_never_available_in_production(
    client: TestClient,
) -> None:
    """The test is on the environment, not on a flag someone could forget to set."""

    production = make_settings(environment="production")

    with _secured_for(production) as production_client:
        response = production_client.post(
            "/api/auth/token", json={"subject": "local-developer"}
        )

    assert response.status_code == 404


# ---------------------------------------------------------------- rate limits


def test_a_caller_is_refused_after_their_limit() -> None:
    """The third request in a window of two is refused.

    This is what stops one caller from spending the deployment's budget, and it is per caller
    rather than global so that a busy colleague does not lock you out.
    """

    limiter = RateLimiter(window_seconds=60)
    limiter.check("caller", "chat", 2)
    limiter.check("caller", "chat", 2)

    with pytest.raises(HTTPException) as refusal:
        limiter.check("caller", "chat", 2)

    assert refusal.value.status_code == 429


def test_two_callers_have_separate_budgets() -> None:
    """One caller exhausting their limit does not refuse the next one."""

    limiter = RateLimiter(window_seconds=60)
    limiter.check("noisy", "chat", 1)

    with pytest.raises(HTTPException):
        limiter.check("noisy", "chat", 1)

    limiter.check("quiet", "chat", 1)


def test_two_families_have_separate_budgets() -> None:
    """Vision and chat are limited separately.

    A caller analysing screenshots should not exhaust their chat budget, and the two operations
    cost different amounts.
    """

    limiter = RateLimiter(window_seconds=60)
    limiter.check("caller", "chat", 1)

    with pytest.raises(HTTPException):
        limiter.check("caller", "chat", 1)

    limiter.check("caller", "vision", 1)


def test_a_limited_caller_is_told_when_to_come_back() -> None:
    """The refusal carries Retry-After, so a client can wait instead of retrying immediately.

    Retrying straight away against a limit is how a limit turns into an outage for everybody.
    """

    limiter = RateLimiter(window_seconds=60)
    limiter.check("caller", "chat", 1)

    with pytest.raises(HTTPException) as refusal:
        limiter.check("caller", "chat", 1)

    assert int(refusal.value.headers["Retry-After"]) >= 1


def test_the_window_rolls_over() -> None:
    """A caller is served again once the window has passed."""

    limiter = RateLimiter(window_seconds=1)
    limiter.check("caller", "chat", 1)

    with pytest.raises(HTTPException):
        limiter.check("caller", "chat", 1)

    time.sleep(1.05)

    limiter.check("caller", "chat", 1)


def test_the_remaining_count_never_goes_below_zero() -> None:
    """A caller over the limit is told zero is left, not a negative number."""

    limiter = RateLimiter(window_seconds=60)

    assert limiter.remaining("caller", "chat", 2) == 2

    limiter.check("caller", "chat", 2)
    limiter.check("caller", "chat", 2)

    with pytest.raises(HTTPException):
        limiter.check("caller", "chat", 2)

    assert limiter.remaining("caller", "chat", 2) == 0


def test_the_endpoint_returns_429_with_a_retry_after(
    client: TestClient, settings: Settings
) -> None:
    """The limit is visible to a client as 429 with a retry hint, not as a generic failure."""

    tight = settings.model_copy(update={"rate_limit_chat_per_minute": 1})

    with _secured_for(tight) as limited:
        limited.post("/api/chat", json={"message": "First"})
        response = limited.post("/api/chat", json={"message": "Second"})

    assert response.status_code == 429
    assert response.headers["retry-after"]


def test_vision_and_chat_are_limited_independently(
    client: TestClient, settings: Settings
) -> None:
    """A vision request is larger and more expensive, so it has its own allowance.

    Exhausting one family must not refuse the other: a caller who has used up their screenshot
    budget should still be able to ask a question.
    """

    rate_limiter.reset()
    exhausted = settings.model_copy(
        update={"rate_limit_vision_per_minute": 1, "rate_limit_chat_per_minute": 5}
    )

    with _secured_for(exhausted) as limited:
        limited.post("/api/vision/analyze", json={"image_base64": png_base64()})
        refused = limited.post("/api/vision/analyze", json={"image_base64": png_base64()})
        allowed = limited.post("/api/chat", json={"message": "Still allowed"})

    assert refused.status_code == 429
    assert allowed.status_code == 200


def test_the_agent_family_is_limited_separately_too(
    client: TestClient, settings: Settings
) -> None:
    """An agent run is several model calls, so it has its own allowance.

    Each run needs a planned step and a summary from the model, so the fake is scripted with both
    and the allowance is set to one: the first run is served and the second is refused.
    """

    rate_limiter.reset()
    tight = settings.model_copy(update={"rate_limit_agent_per_minute": 1})
    scripted = FakeGeminiClient(
        [
            json.dumps(
                {
                    "steps": [
                        {
                            "tool": "document_analysis",
                            "description": "Analyse the text",
                            "arguments": {"text": "some text"},
                        }
                    ]
                }
            ),
            "The analysis.",
            "The answer.",
        ]
    )

    with _secured_for(tight, gemini=make_gemini(tight, scripted)) as limited:
        first = limited.post("/api/agent/run", json={"task": "Analyse this"})
        second = limited.post("/api/agent/run", json={"task": "And this"})

    assert first.status_code == 200
    assert second.status_code == 429


def test_a_limit_of_zero_means_no_limit() -> None:
    """Zero is read as "off" rather than "refuse everything".

    An operator disabling a limit should not accidentally shut the endpoint down.
    """

    limiter = RateLimiter(window_seconds=60)

    for _ in range(50):
        limiter.check("caller", "chat", 0)


# ---------------------------------------------------------------- logging


def test_a_log_line_records_the_request_without_its_content(
    client: TestClient, caplog: pytest.LogCaptureFixture
) -> None:
    """The access log says what happened, and not what was said."""

    with caplog.at_level("INFO", logger="app.access"):
        client.post("/api/chat", json={"message": "My account number is 12345"})

    assert "12345" not in caplog.text
    assert "account number" not in caplog.text
    assert "request_id" in caplog.text
    assert "status=200" in caplog.text


def test_a_log_line_records_no_screenshot(
    client: TestClient, caplog: pytest.LogCaptureFixture
) -> None:
    """A request carrying an image leaves no trace of the image in the log."""

    encoded = png_base64()

    with caplog.at_level("INFO", logger="app.access"):
        client.post("/api/vision/analyze", json={"image_base64": encoded})

    assert encoded[:40] not in caplog.text
    assert "image" not in caplog.text


def test_a_failure_is_logged_with_its_code_and_not_its_content(
    client: TestClient, settings: Settings, caplog: pytest.LogCaptureFixture
) -> None:
    """An error log carries a stable code a client can act on, and no user text."""

    broken = make_gemini(settings, fail_with=TimeoutError("slow"))

    with caplog.at_level("WARNING"):
        with _secured_for(settings, gemini=broken) as failing:
            response = failing.post(
                "/api/chat", json={"message": "a private question about payroll"}
            )

    assert response.status_code == 503
    assert "payroll" not in caplog.text


def test_the_configured_key_never_reaches_the_log(
    client: TestClient, caplog: pytest.LogCaptureFixture
) -> None:
    """The key in the configuration is not written down, even at debug level."""

    with caplog.at_level("DEBUG"):
        client.get("/health")

    assert "sk-test" not in caplog.text


# ---------------------------------------------------------------- helpers


@contextmanager
def _secured_for(settings: Settings, **services: Any) -> Iterator[TestClient]:
    """A client for the given settings, with the fakes injected.

    Only the service a test cares about is passed; the rest are filled in by the shared assembler,
    so a test never reaches the real Gemini client by accident.
    """

    with running_client(settings, **services) as test_client:
        yield test_client
def _segment(payload: dict) -> str:
    """Base64url without padding, as a JWT segment."""

    raw = json.dumps(payload, separators=(",", ":"))

    return base64.urlsafe_b64encode(raw.encode()).decode().rstrip("=")


def _expired_token(tokens: TokenService) -> str:
    """A token with a correct signature and a window that has already closed."""

    import jwt

    claims = {
        "sub": "someone",
        "iss": tokens.settings.jwt_issuer,
        "exp": int(time.time()) - 60,
        "iat": int(time.time()) - 3600,
    }

    return jwt.encode(
        claims,
        _signing_key(tokens.settings),
        algorithm=tokens.settings.jwt_algorithm,
    )
