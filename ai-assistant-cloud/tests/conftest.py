"""Shared fixtures.

The whole suite runs with no credentials, no network and no database. That is a constraint on
purpose: a test that needs a Gemini key is a test that fails on somebody else's machine and gets
skipped, and a skipped privacy test is worse than no privacy test.
"""

from __future__ import annotations

from collections.abc import Iterator
from typing import Any

import pytest
from fastapi.testclient import TestClient

from app.config.settings import Settings
from app.middleware.rate_limit import rate_limiter
from app.middleware.security import TokenService
from app.services.agent_service import AgentService, CloudToolRegistry
from app.services.gemini_service import GeminiService
from app.services.rag_service import RAGService
from tests.fakes import (
    FakeGeminiClient,
    make_embeddings,
    make_gemini,
    make_settings,
    make_vectors,
)
from tests.support import running_client


@pytest.fixture
def settings() -> Settings:
    """Test settings: no credentials, no auth, no timeouts that could surprise a test."""

    return make_settings()


@pytest.fixture(autouse=True)
def clean_rate_limits() -> Iterator[None]:
    """Clears the rate limiter between tests.

    The counters are module level and shared, so without this one test's requests would cause
    another's 429 and the failure would look like a bug in the endpoint.
    """

    rate_limiter.reset()
    yield
    rate_limiter.reset()


@pytest.fixture
def vectors():
    """An in-process vector store."""

    return make_vectors()


@pytest.fixture
def gemini(settings) -> GeminiService:
    """A Gemini service with the fake client."""

    return make_gemini(settings)


@pytest.fixture
def embeddings(settings):
    """An embedding service with the fake client."""

    return make_embeddings(settings)


@pytest.fixture
def rag(settings, gemini, embeddings, vectors) -> RAGService:
    """A retrieval service wired to fakes."""

    return RAGService(settings, gemini=gemini, embeddings=embeddings, vectors=vectors)


@pytest.fixture
def agent(settings, gemini, rag, embeddings):
    """An agent service wired to fakes."""

    return AgentService(settings, gemini=gemini, rag=rag, embeddings=embeddings)


@pytest.fixture
def client(settings, gemini, embeddings, vectors, rag, agent) -> Iterator[TestClient]:
    """A test client over an application whose services are all fakes."""

    with running_client(
        settings,
        gemini=gemini,
        embeddings=embeddings,
        vectors=vectors,
        rag=rag,
        agent=agent,
    ) as test_client:
        yield test_client


@pytest.fixture
def authenticated(settings, gemini, embeddings, vectors, rag, agent) -> Iterator[tuple[TestClient, Settings]]:
    """A test client with authentication switched on, which is the deployed configuration.

    Built from the test settings with one field changed rather than from a separate object, so the
    secured client differs from the open one in exactly that field. The settings come back with it
    because a test that issues a token needs the same secret the routes check against.
    """

    secured = settings.model_copy(update={"require_authentication": True})

    with running_client(
        secured,
        gemini=gemini,
        embeddings=embeddings,
        vectors=vectors,
        rag=rag,
        agent=agent,
    ) as test_client:
        yield test_client, secured


@pytest.fixture
def token_client(settings, gemini, embeddings, vectors, rag, agent) -> Iterator[TestClient]:
    """A test client with authentication on, carrying a valid token for it."""

    secured = settings.model_copy(update={"require_authentication": True})

    with running_client(
        secured,
        gemini=gemini,
        embeddings=embeddings,
        vectors=vectors,
        rag=rag,
        agent=agent,
    ) as test_client:
        token, _ = TokenService(secured).issue("test-user")
        test_client.headers.update({"Authorization": f"Bearer {token}"})

        yield test_client


@pytest.fixture
def scripted_gemini(settings):
    """A factory for a Gemini service with specific scripted replies."""

    def factory(replies: list[str], **kwargs: Any) -> GeminiService:
        return GeminiService(settings, FakeGeminiClient(replies, **kwargs))

    return factory


@pytest.fixture
def registry() -> CloudToolRegistry:
    """An empty tool registry."""

    return CloudToolRegistry()
