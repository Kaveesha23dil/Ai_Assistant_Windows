"""Text to vectors, through the Gemini embedding model.

Kept separate from the chat service on purpose. The two models are named independently and are
replaced on different schedules, and an embedding model change invalidates every stored vector.
Coupling them would mean a chat model upgrade quietly requiring a re-embed of the whole
knowledge base.
"""

from __future__ import annotations

import asyncio
import logging
import math
import time
from dataclasses import dataclass
from typing import Any, Protocol

from app.config.settings import Settings, get_settings
from app.services.gemini_service import GeminiUnavailableError

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class EmbeddingBatch:
    """Vectors for a batch of texts, in the order they were given."""

    embeddings: list[list[float]]
    model: str
    tokens_used: int = 0
    latency_ms: int = 0

    @property
    def dimensions(self) -> int:
        """The width of every vector in the batch.

        A batch where the vectors differ in width is a bug rather than a condition to handle, so
        this is asserted by the service before it returns.
        """

        return len(self.embeddings[0]) if self.embeddings else 0


class EmbeddingClient(Protocol):
    """The slice of the embedding SDK this service uses."""

    async def embed(self, **kwargs: Any) -> Any:
        """Embed a batch of texts."""


class _SdkEmbeddingClient:
    """The real embedding client, built lazily so import never needs a credential."""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def _build(self) -> Any:
        try:
            from google import genai  # type: ignore[import-not-found]
        except ImportError as error:  # pragma: no cover - depends on the image
            raise GeminiUnavailableError(
                "The Gemini SDK is not installed in this image.", "GEMINI_SDK_MISSING"
            ) from error

        if self._settings.uses_vertex_ai:
            return genai.Client(
                vertexai=True,
                project=self._settings.project_id,
                location=self._settings.region,
            )

        return genai.Client(api_key=self._settings.gemini_api_key)

    async def embed(self, **kwargs: Any) -> Any:
        client = self._build()

        return await client.aio.models.embed_content(**kwargs)


class EmbeddingService:
    """Turns text into normalised vectors."""

    def __init__(
        self,
        settings: Settings | None = None,
        client: EmbeddingClient | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._client = client or _SdkEmbeddingClient(self._settings)
        self._last_tokens = 0

    @property
    def last_tokens_used(self) -> int:
        """Tokens spent by the most recent batch.

        Reported separately from the returned batch because a caller that embeds once to index a
        document and once to run a query is doing both, and a report of only the second is a
        report of the wrong thing.
        """

        return self._last_tokens

    @property
    def model_name(self) -> str:
        """The configured embedding model."""

        return self._settings.gemini_embedding_model_name

    async def embed_texts(
        self, texts: list[str], *, input_type: str = "query"
    ) -> EmbeddingBatch:
        """Embeds a batch of texts.

        The returned vectors are L2-normalised, which is what lets a similarity be a dot product
        and removes a class of bug where a long document scores highly simply by being long.
        """

        if not texts:
            return EmbeddingBatch(embeddings=[], model=self.model_name)

        started = time.perf_counter()

        try:
            response = await asyncio.wait_for(
                self._client.embed(
                    model=self._settings.gemini_embedding_model_name,
                    contents=texts,
                    config={
                        "task_type": (
                            "RETRIEVAL_DOCUMENT" if input_type == "document" else "RETRIEVAL_QUERY"
                        )
                    },
                ),
                timeout=self._settings.gemini_timeout_seconds,
            )
        except asyncio.TimeoutError as error:
            raise GeminiUnavailableError(
                "The embedding model did not answer in time.", "EMBEDDING_TIMEOUT"
            ) from error
        except GeminiUnavailableError:
            raise
        except Exception as error:  # noqa: BLE001
            logger.error("An embedding request failed with %s.", type(error).__name__)
            raise GeminiUnavailableError(
                "The embedding model could not be reached.", "EMBEDDING_REQUEST_FAILED"
            ) from error

        vectors = self._read_embeddings(response)

        if len(vectors) != len(texts):
            # Returning the wrong number of vectors would misalign every index between a chunk and
            # its vector, and the failure would appear much later as an unrelated answer.
            raise GeminiUnavailableError(
                "The embedding model returned an unexpected number of vectors.",
                "EMBEDDING_COUNT_MISMATCH",
            )

        normalised = [self._normalize(vector) for vector in vectors]

        self._last_tokens = self._read_tokens(response)

        return EmbeddingBatch(
            embeddings=normalised,
            model=self.model_name,
            tokens_used=self._last_tokens,
            latency_ms=max(0, int((time.perf_counter() - started) * 1000)),
        )

    async def embed_query(self, text: str) -> list[float]:
        """Embeds one question."""

        batch = await self.embed_texts([text], input_type="query")

        return batch.embeddings[0]

    async def embed_document(self, text: str) -> list[float]:
        """Embeds one passage for storage."""

        batch = await self.embed_texts([text], input_type="document")

        return batch.embeddings[0]

    # ------------------------------------------------------------------ internals

    @staticmethod
    def _read_embeddings(response: Any) -> list[list[float]]:
        """Pulls the vectors out of a response, whatever shape the SDK returned."""

        values = getattr(response, "embeddings", None)

        if values is None and isinstance(response, dict):
            values = response.get("embeddings")

        if values is None:
            return []

        vectors: list[list[float]] = []

        for item in values:
            raw = getattr(item, "values", None)

            if raw is None and isinstance(item, dict):
                raw = item.get("values")

            if raw is not None:
                vectors.append([float(value) for value in raw])

        return vectors

    @staticmethod
    def _read_tokens(response: Any) -> int:
        """Reads the token usage, defaulting to zero."""

        usage = getattr(response, "metadata", None) or getattr(response, "usage_metadata", None)

        if usage is None and isinstance(response, dict):
            usage = response.get("metadata") or response.get("usage_metadata")

        total = getattr(usage, "total_token_count", None)

        if total is None and isinstance(usage, dict):
            total = usage.get("total_token_count")

        return int(total) if isinstance(total, int) else 0

    @staticmethod
    def _normalize(vector: list[float]) -> list[float]:
        """Scales a vector to unit length.

        A zero vector is left alone rather than divided by, because there is no direction to
        preserve and a caller comparing it needs to see the zeros rather than a division error.
        """

        length = math.sqrt(sum(component * component for component in vector))

        if length == 0.0:
            return vector

        return [component / length for component in vector]
