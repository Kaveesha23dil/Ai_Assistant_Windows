"""Embeddings: the vectors have to be comparable, and the batch has to line up.

A vector that is not normalised, or a batch whose vectors come back in a different order from the
texts, produces a knowledge base that retrieves confidently and wrongly. Both are silent, so both
are pinned down here.
"""

from __future__ import annotations

import pytest

from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiUnavailableError
from tests.fakes import FakeEmbeddingClient, make_embeddings


def service_for(settings, client: FakeEmbeddingClient | None = None) -> EmbeddingService:
    """An embedding service with the fake client."""

    return EmbeddingService(
        settings, client=client or FakeEmbeddingClient()
    )


# ---------------------------------------------------------------- shape


@pytest.mark.asyncio
async def test_a_batch_comes_back_in_the_order_it_went_in(settings) -> None:
    """Vector i belongs to text i.

    A reordering would be invisible to a shape assertion and catastrophic to a knowledge base: the
    text would be attached to somebody else's vector and every later retrieval would be wrong.
    """

    service = service_for(settings)

    first = await service.embed_texts(["alpha", "beta"])
    second = await service.embed_texts(["beta", "alpha"])

    assert first.embeddings[0] == second.embeddings[1]
    assert first.embeddings[1] == second.embeddings[0]


@pytest.mark.asyncio
async def test_every_vector_has_the_same_width(settings) -> None:
    """A batch with mixed widths cannot be searched."""

    batch = await service_for(settings).embed_texts(["one", "two", "three"])

    widths = {len(vector) for vector in batch.embeddings}

    assert len(widths) == 1
    assert batch.dimensions == widths.pop()


@pytest.mark.asyncio
async def test_the_same_text_gives_the_same_vector(settings) -> None:
    """Determinism, or the index and the query side could not be compared at all."""

    service = service_for(settings)

    assert await service.embed_document("the same text") == await service.embed_document(
        "the same text"
    )


@pytest.mark.asyncio
async def test_different_texts_give_different_vectors(settings) -> None:
    """Two texts are not silently embedded as the same thing."""

    batch = await service_for(settings).embed_texts(["refund policy", "opening hours"])

    assert batch.embeddings[0] != batch.embeddings[1]


@pytest.mark.asyncio
async def test_a_document_and_a_query_are_embedded_differently(settings) -> None:
    """The task type is sent, because a document and a query are not the same kind of text.

    Gemini's embedding models are trained for asymmetric retrieval and take a different task type
    for each side. Sending the wrong one still returns vectors, which is exactly why it needs a
    test rather than a comment.
    """

    client = FakeEmbeddingClient()
    service = service_for(settings, client)

    await service.embed_document("a document")
    await service.embed_query("a question")

    assert client.calls[0]["config"]["task_type"] == "RETRIEVAL_DOCUMENT"
    assert client.calls[1]["config"]["task_type"] == "RETRIEVAL_QUERY"


# ---------------------------------------------------------------- normalisation


@pytest.mark.asyncio
async def test_vectors_are_unit_length(settings) -> None:
    """Normalised, so a similarity is a dot product and length cannot inflate a score."""

    batch = await service_for(settings).embed_texts(["a", "a much longer text than the first"])

    for vector in batch.embeddings:
        length = sum(component * component for component in vector) ** 0.5

        assert length == pytest.approx(1.0, abs=1e-9)


@pytest.mark.asyncio
async def test_a_long_text_is_no_closer_merely_for_being_long(settings) -> None:
    """A document repeated thirty times should not out-score a single mention.

    This is the failure that normalisation exists to prevent, and it is worth a test because it is
    invisible until somebody indexes something repetitive.
    """

    batch = await service_for(settings).embed_texts(
        ["refund", "refund " * 30, "completely unrelated words entirely"]
    )

    short, long, other = batch.embeddings

    def dot(left: list[float], right: list[float]) -> float:
        return sum(a * b for a, b in zip(left, right, strict=True))

    assert dot(short, long) == pytest.approx(1.0, abs=0.05)
    assert dot(short, other) < dot(short, long)


# ---------------------------------------------------------------- single values


@pytest.mark.asyncio
async def test_a_query_returns_one_vector(settings) -> None:
    """The single-value helper is what the routes use, so it must not return a batch."""

    vector = await service_for(settings).embed_query("a question")

    assert isinstance(vector, list)
    assert vector and isinstance(vector[0], float)


@pytest.mark.asyncio
async def test_an_empty_batch_returns_nothing_rather_than_raising(settings) -> None:
    """Indexing an empty document is a no-op, not an error.

    The route that accepts a document will receive an empty one from somebody eventually, and a
    crash on that is a poor trade for one wasted call.
    """

    batch = await service_for(settings).embed_texts([])

    assert batch.embeddings == []
    assert batch.dimensions == 0


# ---------------------------------------------------------------- accounting and failures


@pytest.mark.asyncio
async def test_the_batch_reports_what_it_spent(settings) -> None:
    """Indexing a knowledge base is a cost, and the caller needs to see it."""

    batch = await service_for(settings, FakeEmbeddingClient(tokens=11)).embed_texts(["a"])

    assert batch.tokens_used == 11


@pytest.mark.asyncio
async def test_the_last_call_is_reported_for_a_tool_that_searched(settings) -> None:
    """A search reports its embedding cost, which is otherwise invisible."""

    service = service_for(settings, FakeEmbeddingClient(tokens=9))

    await service.embed_query("a question")

    assert service.last_tokens_used == 9


@pytest.mark.asyncio
async def test_a_slow_model_is_reported_as_unavailable(settings) -> None:
    """A timeout has to surface as a code the caller can fall back on.

    The desktop client's fallback depends on this being an ordinary, catchable failure rather than
    a hang: an indexing request that never returns would leave the UI waiting with nothing to say.
    """

    impatient = settings.model_copy(update={"gemini_timeout_seconds": 0.01})
    service = service_for(impatient, FakeEmbeddingClient(delay=0.5))

    with pytest.raises(GeminiUnavailableError) as failure:
        await service.embed_texts(["a"])

    assert failure.value.code == "EMBEDDING_TIMEOUT"


@pytest.mark.asyncio
async def test_a_refused_model_call_does_not_leak_the_model_s_message(settings) -> None:
    """The exception a client sees is a stable code, not the provider's error text.

    A provider message can contain the request, and the request is somebody's document.
    """

    service = service_for(
        settings, FakeEmbeddingClient(fail_with=RuntimeError("key sk-abc rejected for prompt"))
    )

    with pytest.raises(GeminiUnavailableError) as failure:
        await service.embed_texts(["a private document"])

    assert "sk-abc" not in str(failure.value)
    assert failure.value.code == "EMBEDDING_REQUEST_FAILED"


def test_the_model_name_comes_from_configuration(settings) -> None:
    """The embedding model is named independently of the chat model."""

    configured = settings.model_copy(update={"gemini_embedding_model_name": "text-embedding-005"})

    assert service_for(configured).model_name == "text-embedding-005"


def test_the_embedding_model_is_not_the_chat_model() -> None:
    """A configuration where they are the same is a mistake worth catching early."""

    assert make_embeddings().model_name != "gemini-2.0-flash"
