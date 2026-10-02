"""Building a test application out of fakes.

Split out of ``conftest`` because the test modules need these too, and a conftest is not
importable by name. The point of the split is that the container is assembled by hand: a test
cannot reach the real Gemini client by asking for a service it forgot to replace.
"""

from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager
from typing import Any

from fastapi.testclient import TestClient

from app.config.settings import Settings
from app.main import create_app
from app.services.agent_service import AgentService
from app.services.container import ServiceContainer, set_container
from app.services.rag_service import RAGService
from tests.fakes import make_embeddings, make_gemini, make_vectors


def build_container(
    settings: Settings,
    gemini: Any,
    embeddings: Any,
    vectors: Any,
    rag: Any,
    agent: Any,
) -> ServiceContainer:
    """A container whose services are all the fakes.

    Built by bypassing ``ServiceContainer``'s constructor, which would otherwise construct the real
    Gemini client and the real vector store.
    """

    container = ServiceContainer.__new__(ServiceContainer)
    container._settings = settings
    container._vectors = vectors
    container._gemini = gemini
    container._embeddings = embeddings
    container._rag = rag
    container._agent = agent

    return container


def assemble(settings: Settings, **services: Any) -> dict[str, Any]:
    """Fills in any service the caller did not supply.

    A test only passes the one it cares about. Everything else is still a fake, because a real
    client would need a credential and would fail for the wrong reason.
    """

    gemini = services.get("gemini") or make_gemini(settings)
    embeddings = services.get("embeddings") or make_embeddings(settings)
    vectors = services.get("vectors") or make_vectors()
    rag = services.get("rag") or RAGService(
        settings, gemini=gemini, embeddings=embeddings, vectors=vectors
    )
    agent = services.get("agent") or AgentService(
        settings, gemini=gemini, rag=rag, embeddings=embeddings
    )

    return {
        "gemini": gemini,
        "embeddings": embeddings,
        "vectors": vectors,
        "rag": rag,
        "agent": agent,
    }


@contextmanager
def running_client(settings: Settings, **services: Any) -> Iterator[TestClient]:
    """A live test client over an application built from fakes, for the given settings."""

    container = build_container(settings, **assemble(settings, **services))
    set_container(container)

    try:
        with TestClient(create_app(settings)) as test_client:
            yield test_client
    finally:
        set_container(None)
