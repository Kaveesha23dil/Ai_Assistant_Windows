"""Fake Gemini and embedding clients.

Every test that exercises a route or a service uses these rather than the real SDK. The point is
not only that the tests are fast: it is that a test suite which can reach the network can pass
because a vendor model happened to answer well, and can fail because a vendor changed a word in
a prompt. Neither is information about this code.
"""

from __future__ import annotations

import asyncio
import re
from typing import Any

from app.config.settings import Settings
from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiResult, GeminiService
from app.services.vector_database import InMemoryVectorDatabase

_STOPWORDS = frozenset(
    """
    a about an and any are as at be been but by can did do does for from had has have how in
    is it its of on or that the their there this to was were what when where which who why will
    with you your
    """.split()
)
"""Words carrying no topical signal.

Without these, every question and every document shares a handful of buckets and their vectors end
up closer than the pairs that are actually about the same thing.
"""


def make_settings(**overrides: Any) -> Settings:
    """Settings for a test: no credentials, no auth, no timeout surprises."""

    base: dict[str, Any] = {
        "gemini_api_key": "",
        "project_id": "",
        "vector_store": "in-memory",
        "region": "us-central1",
        "jwt_secret": "test-secret-value-that-is-long-enough-to-pass-validation",
        "require_authentication": False,
        "environment": "development",
        "gemini_timeout_seconds": 5.0,
        "log_request_bodies": False,
    }

    base.update(overrides)

    return Settings(**base)


class FakeGeminiClient:
    """Returns scripted answers and records what it was asked."""

    def __init__(
        self,
        replies: list[str] | None = None,
        *,
        fail_with: Exception | None = None,
        delay: float = 0.0,
        tokens: int = 42,
    ) -> None:
        self._replies = list(replies or ["An answer."])
        self._fail_with = fail_with
        self._delay = delay
        self._tokens = tokens
        self._index = 0
        self.calls: list[dict[str, Any]] = []

    async def generate(self, **kwargs: Any) -> Any:
        """Records the call and returns the next scripted reply."""

        self.calls.append(kwargs)

        if self._delay:
            await asyncio.sleep(self._delay)

        if self._fail_with is not None:
            raise self._fail_with

        reply = self._replies[min(self._index, len(self._replies) - 1)]
        self._index += 1

        return _FakeResponse(reply, self._tokens)

    async def stream_generate(self, **kwargs: Any):
        """Records the call and yields the next scripted reply in pieces."""

        self.calls.append(kwargs)

        if self._fail_with is not None:
            raise self._fail_with

        reply = self._replies[min(self._index, len(self._replies) - 1)]
        self._index += 1

        # Split on spaces so the deltas look like tokens rather than one lump, which is what a
        # streaming test has to cope with.
        pieces = reply.split(" ")

        for position, piece in enumerate(pieces):
            if self._delay:
                await asyncio.sleep(self._delay)

            yield _FakeResponse(piece + (" " if position < len(pieces) - 1 else ""), self._tokens)


class _FakeResponse:
    """The shape the Gemini service reads, with several accessors so both paths are exercised."""

    def __init__(self, text: str, tokens: int) -> None:
        self.text = text
        self.usage_metadata = _Usage(tokens)
        self.candidates = [_Candidate()]


class _Usage:
    """Token usage."""

    def __init__(self, tokens: int) -> None:
        self.total_token_count = tokens


class _Candidate:
    """A candidate with no finish reason override."""

    finish_reason = None


class FakeEmbeddingClient:
    """Returns deterministic vectors derived from the words in the text.

    Deterministic, and lexical on purpose. Retrieval here is being tested as retrieval: a fake
    that returned unrelated vectors would make every similarity assertion meaningless, and one that
    returned genuinely semantic vectors would be pretending to be a model. This one reproduces the
    property a store depends on, that texts sharing words are closer than texts that do not, so a
    test can assert a passage was found and an irrelevant one was not.

    The hashing is by MD5 rather than ``hash`` because Python randomises string hashing per
    process: a test that passed on one machine and failed on the next would be the worst outcome.
    """

    def __init__(self, dimensions: int = 64, tokens: int = 7, delay: float = 0.0, fail_with: Exception | None = None) -> None:
        self._dimensions = dimensions
        self._tokens = tokens
        self._delay = delay
        self._fail_with = fail_with
        self.calls: list[dict[str, Any]] = []

    async def embed(self, **kwargs: Any) -> Any:
        """Records the call and returns one deterministic vector per text."""

        self.calls.append(kwargs)

        if self._delay:
            await asyncio.sleep(self._delay)

        if self._fail_with is not None:
            raise self._fail_with

        texts = kwargs.get("contents", [])

        return _FakeEmbeddingResponse(
            [self._vector_for(text) for text in texts], self._tokens
        )

    def _vector_for(self, text: str) -> list[float]:
        """A unit vector whose direction depends on which words the text contains."""

        vector = [0.0] * self._dimensions

        for word in _significant_words(text):
            vector[_bucket(word, self._dimensions)] += 1.0

        length = sum(component * component for component in vector) ** 0.5

        if length == 0.0:
            vector[0] = 1.0
            return vector

        return [component / length for component in vector]


def _bucket(word: str, dimensions: int) -> int:
    """Which dimension a word falls into."""

    import hashlib

    digest = hashlib.md5(word.encode("utf-8"), usedforsecurity=False).digest()

    return int.from_bytes(digest[:4], "big") % dimensions


def _significant_words(text: str) -> list[str]:
    """The content words of a text, crudely stemmed.

    Plurals are folded together because "refund" and "refunds" appearing in a question and a
    document should count as the same word, and a test that had to write the plural everywhere
    would read as though the system only understood one form.
    """

    words = re.findall(r"[a-z0-9]+", text.lower())
    kept = [
        word[:-1] if len(word) > 3 and word.endswith("s") else word
        for word in words
        if word not in _STOPWORDS
    ]

    return kept or words or ["empty"]


class _FakeEmbeddingResponse:
    """The shape the embedding service reads."""

    def __init__(self, embeddings: list[list[float]], tokens: int) -> None:
        self.embeddings = [_Embedding(vector) for vector in embeddings]
        self.metadata = _Usage(tokens)


class _Embedding:
    """One embedding value."""

    def __init__(self, values: list[float]) -> None:
        self.values = values


def make_gemini(
    settings: Settings | None = None,
    replies: list[str] | str | FakeGeminiClient | None = None,
    **kwargs: Any,
) -> GeminiService:
    """A Gemini service backed by the fake client.

    Accepts a ready-made client as well as a list of replies, because a test that scripts a
    sequence needs to hold on to the client to inspect what it was asked.
    """

    client = replies if isinstance(replies, FakeGeminiClient) else FakeGeminiClient(replies, **kwargs)

    return GeminiService(settings or make_settings(), client)


def make_embeddings(
    settings: Settings | None = None, dimensions: int = 64
) -> EmbeddingService:
    """An embedding service backed by the fake client."""

    return EmbeddingService(
        settings or make_settings(), FakeEmbeddingClient(dimensions=dimensions)
    )


def make_vectors(dimensions: int = 64) -> InMemoryVectorDatabase:
    """An in-process vector store."""

    return InMemoryVectorDatabase(expected_dimensions=dimensions)


def result(text: str = "An answer.", tokens: int = 5) -> GeminiResult:
    """A Gemini result for a test that stubs the service itself."""

    return GeminiResult(text=text, model="test-model", tokens_used=tokens)
