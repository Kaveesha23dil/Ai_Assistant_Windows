"""Shared service access.

The services are built once per process and attached to the application state, rather than being
constructed per request. A service holds no per-request state, so a fresh one per request would
work, but it would also mean a settings read and a lock per request for no benefit, and the
agent's tool registry would be rebuilt for every task.
"""

from __future__ import annotations

import logging

from app.config.settings import Settings, get_settings
from app.services.agent_service import AgentService, CloudToolRegistry
from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiService
from app.services.rag_service import RAGService
from app.services.vector_database import (
    IVectorDatabase,
    InMemoryVectorDatabase,
    VertexVectorDatabase,
)

logger = logging.getLogger(__name__)


class ServiceContainer:
    """The services, built once."""

    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()
        self._vectors = build_vector_database(self._settings)
        self._gemini = GeminiService(self._settings)
        self._embeddings = EmbeddingService(self._settings)
        self._rag = RAGService(
            self._settings,
            gemini=self._gemini,
            embeddings=self._embeddings,
            vectors=self._vectors,
        )
        self._agent = AgentService(
            self._settings,
            gemini=self._gemini,
            rag=self._rag,
            embeddings=self._embeddings,
        )

    @property
    def settings(self) -> Settings:
        """The settings in use."""

        return self._settings
    @property
    def vectors(self) -> IVectorDatabase:
        """The vector store in use."""

        return self._vectors

    @property
    def gemini(self) -> GeminiService:
        """The Gemini service."""

        return self._gemini

    @property
    def embeddings(self) -> EmbeddingService:
        """The embedding service."""

        return self._embeddings

    @property
    def rag(self) -> RAGService:
        """The retrieval service."""

        return self._rag

    @property
    def agent(self) -> AgentService:
        """The agent service."""

        return self._agent

    @property
    def tool_registry(self) -> CloudToolRegistry:
        """The agent's tool registry."""

        return self._agent.registry


def build_vector_database(settings: Settings) -> IVectorDatabase:
    """Chooses the vector store from configuration.

    The choice is made by ``VECTOR_STORE`` and by nothing else. Inferring it from whether a project
    id happens to be set would be worse than it sounds: ``PROJECT_ID`` is also what the Vertex AI
    model calls need, so the configuration that authenticates the model to Vertex would silently
    turn on a vector store the operator never asked for, and the first failure would arrive on the
    first search rather than at startup.

    Nothing above this function names a concrete store, so this is the only place that changes to
    move to a different one.
    """

    if settings.vector_store == "in-memory":
        logger.info(
            "Using the in-process vector store. Nothing is persisted between restarts."
        )

        return InMemoryVectorDatabase()

    if not settings.vector_search_index_id:
        # Falling back rather than failing to start: an empty index would answer every question
        # from general knowledge, which is a worse outcome than an empty knowledge base, so this is
        # logged loudly and the health check reports it as a configuration problem.
        logger.error(
            "VECTOR_STORE is %r but VECTOR_SEARCH_INDEX_ID is empty, so the in-process store is "
            "used instead. Every question will be answered without the knowledge base.",
            settings.vector_store,
        )

        return InMemoryVectorDatabase()

    logger.info(
        "Using Vertex AI Vector Search index %s in %s.",
        settings.vector_search_index_id,
        settings.region,
    )

    return VertexVectorDatabase(
        project_id=settings.project_id,
        region=settings.region,
        index_id=settings.vector_search_index_id,
        endpoint_override=settings.vector_search_endpoint,
    )


_container: ServiceContainer | None = None


def get_container() -> ServiceContainer:
    """The container for this process."""

    global _container

    if _container is None:
        _container = ServiceContainer()

    return _container


def set_container(container: ServiceContainer | None) -> None:
    """Replaces the container, for a test that needs services with fake clients."""

    global _container

    _container = container
