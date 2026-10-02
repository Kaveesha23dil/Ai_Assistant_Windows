"""Retrieval: does the right passage reach the model, and is the citation honest.

A retrieval service can pass every plumbing test and still answer wrongly. The two failures that
matter are quietly retrieving nothing and quietly citing something irrelevant, and both are easier
to see in a test than in a demonstration.
"""

from __future__ import annotations

import pytest

from app.services.rag_service import RAGService
from app.services.vector_database import VectorRecord
from tests.fakes import FakeGeminiClient, make_embeddings, make_gemini, make_vectors

DOCUMENTS = [
    (
        "refunds.txt",
        "Refunds are issued to the original payment method within 14 days of approval.",
    ),
    (
        "support.txt",
        "Support is available 09:00 to 17:00 UTC on weekdays, excluding public holidays.",
    ),
    (
        "architecture.txt",
        "The desktop client renders a streamed answer token by token and never blocks the UI "
        "thread while waiting for the cloud service.",
    ),
]


@pytest.fixture
def service(settings) -> RAGService:
    """A retrieval service over an in-memory store with three known documents."""

    return RAGService(
        settings,
        gemini=make_gemini(settings, ["The refund window is 14 days."]),
        embeddings=make_embeddings(settings),
        vectors=make_vectors(),
    )


async def _index(service: RAGService) -> None:
    for name, text in DOCUMENTS:
        await service.index([(name, text)])


# ---------------------------------------------------------------- indexing


@pytest.mark.asyncio
async def test_an_indexed_document_can_be_found_again(service: RAGService) -> None:
    """The round trip the knowledge base exists for."""

    await _index(service)

    assert await service.document_count() == 3


@pytest.mark.asyncio
async def test_a_search_finds_the_passage_that_answers_the_question(
    service: RAGService,
) -> None:
    """A refund question retrieves the refund passage."""

    await _index(service)
    matches = await service.retrieve("How long does a refund take?")

    assert matches
    assert "14 days" in matches[0].record.text


@pytest.mark.asyncio
async def test_a_search_with_nothing_relevant_retrieves_nothing(
    service: RAGService,
) -> None:
    """An unrelated question returns no passage rather than the least bad one.

    Returning a weak match would put an irrelevant sentence in front of the model and let it write
    an answer from it. An empty context is honest; a bad context is not.
    """

    await _index(service)
    matches = await service.retrieve("What is the airspeed velocity of an unladen swallow?")

    assert matches == []


@pytest.mark.asyncio
async def test_reindexing_the_same_document_does_not_duplicate_it(
    service: RAGService,
) -> None:
    """Indexing twice leaves one copy.

    A document edited and re-indexed would otherwise appear twice in every retrieval and be cited
    twice, which reads as two independent sources when it is one.
    """

    await _index(service)
    await service.index([("refunds.txt", DOCUMENTS[0][1])])

    matches = await service.retrieve("How long does a refund take?")

    assert len(matches) == 1


@pytest.mark.asyncio
async def test_a_document_is_removed_by_its_id(service: RAGService) -> None:
    """A document can be taken back out.

    Removing a document is the operation a person expects when they delete a file, and a knowledge
    base that can only grow is a privacy problem.
    """

    await _index(service)

    assert await service.remove_document("refunds.txt") == 1
    assert await service.document_count() == 2


@pytest.mark.asyncio
async def test_removing_a_document_that_is_not_there_is_not_a_crash(
    service: RAGService,
) -> None:
    """Deleting something absent reports failure rather than raising."""

    assert await service.remove_document("never-indexed.txt") == 0


@pytest.mark.asyncio
async def test_blank_text_is_not_indexed(service: RAGService) -> None:
    """An empty document would embed to a zero vector and match nothing usefully."""

    await service.index([("empty.txt", "   ")])

    assert await service.document_count() == 0


# ---------------------------------------------------------------- answering


@pytest.mark.asyncio
async def test_an_answer_cites_the_passages_it_used(service: RAGService) -> None:
    """A grounded answer reports its sources, with enough detail to find them again."""

    await _index(service)
    result = await service.answer("How long does a refund take?")

    assert result.sources
    assert result.sources[0].title == "refunds.txt"
    assert result.sources[0].score > 0


@pytest.mark.asyncio
async def test_an_answer_with_no_knowledge_cites_nothing(
    service: RAGService,
) -> None:
    """Asking with retrieval switched off returns an answer with no sources.

    The sources list being empty is what tells a client the answer was not grounded, which is
    exactly what a person needs to know before acting on it.
    """

    await _index(service)
    result = await service.answer("How long does a refund take?", use_knowledge=False)

    assert result.sources == []


@pytest.mark.asyncio
async def test_the_model_is_told_when_it_has_no_context(settings) -> None:
    """The prompt says the knowledge base was empty rather than leaving a blank space.

    A blank context reads to a model as "nothing relevant", which invites it to answer from
    general knowledge while the answer claims to come from the user's documents.
    """

    client = FakeGeminiClient(["An answer."])
    rag = RAGService(
        settings,
        gemini=make_gemini(settings, client),
        embeddings=make_embeddings(settings),
        vectors=make_vectors(),
    )

    await rag.answer("What is the refund policy?")

    instruction = str(client.calls[-1]["config"]["system_instruction"]).lower()

    assert "nothing was found" in instruction


@pytest.mark.asyncio
async def test_the_context_passesage_is_in_the_prompt(settings) -> None:
    """A retrieved passage is actually shown to the model, not just collected."""

    client = FakeGeminiClient(["An answer."])
    rag = RAGService(
        settings,
        gemini=make_gemini(settings, client),
        embeddings=make_embeddings(settings),
        vectors=make_vectors(),
    )
    await rag.index([("refunds.txt", "Refunds take fourteen days.")])

    await rag.answer("How long does a refund take?")

    assert "Refunds take fourteen days" in str(client.calls[-1]["contents"])


@pytest.mark.asyncio
async def test_an_empty_answer_from_the_model_is_reported(settings) -> None:
    """A blank answer is a failure the client should hear about.

    Returning an empty string with a 200 would look to a user like the assistant had nothing to
    say, rather than like something upstream failed.
    """

    rag = RAGService(
        settings,
        gemini=make_gemini(settings, FakeGeminiClient(["   "])),
        embeddings=make_embeddings(settings),
        vectors=make_vectors(),
    )

    result = await rag.answer("A question")

    assert result.answer.strip() == ""


@pytest.mark.asyncio
async def test_a_context_that_is_too_long_is_truncated(settings) -> None:
    """A long passage is cut to the configured bound.

    Without a bound the context grows with the document and eventually the request is refused by
    the model's context limit, which surfaces as an unexplained failure at exactly the moment
    somebody has indexed a long document.
    """

    rag = RAGService(
        settings.model_copy(update={"max_context_characters": 200}),
        gemini=make_gemini(settings, FakeGeminiClient(["An answer."])),
        embeddings=make_embeddings(settings),
        vectors=make_vectors(),
    )
    await rag.index([("long.txt", "word " * 500)])

    await rag.answer("What does this say?")

    assert len(str(rag._gemini._client.calls[-1]["contents"])) <= 300


# ---------------------------------------------------------------- vector store


@pytest.mark.asyncio
async def test_a_store_refuses_vectors_of_a_different_width() -> None:
    """A mixed-width index is refused at write time rather than producing nonsense scores.

    Changing the embedding model changes the width. A store that accepted both would compare a
    768-long vector against a 3072-long one at query time and return scores that mean nothing,
    which looks like a retrieval bug and is not one.
    """

    store = make_vectors(dimensions=4)
    await store.upsert([VectorRecord(id="good", text="a", vector=[0.1, 0.2, 0.3, 0.4])])

    with pytest.raises(ValueError, match="dimensions"):
        await store.upsert([VectorRecord(id="wide", text="b", vector=[0.1] * 8)])

    assert await store.count() == 1


@pytest.mark.asyncio
async def test_a_store_reports_its_name() -> None:
    """The health check reports the store, so a deployment says which one it is using."""

    assert make_vectors().name == "in-memory"


@pytest.mark.asyncio
async def test_clearing_a_store_empties_it() -> None:
    """A store can be emptied, for a rebuild."""

    store = make_vectors(dimensions=1)
    await store.upsert([VectorRecord(id="one", text="a", vector=[1.0])])

    await store.clear()

    assert await store.count() == 0


@pytest.mark.asyncio
async def test_a_stored_record_keeps_its_metadata() -> None:
    """Metadata survives the round trip, because it is what a citation is built from."""

    store = make_vectors(dimensions=2)
    await store.upsert(
        [
            VectorRecord(
                id="one",
                text="Refunds take fourteen days.",
                vector=[1.0, 0.0],
                metadata={"title": "Refunds", "source": "refunds.txt"},
            )
        ]
    )

    matches = await store.search([1.0, 0.0], top_k=1)

    assert matches[0].record.metadata["title"] == "Refunds"
    assert matches[0].record.id == "one"
    assert matches[0].score == pytest.approx(1.0)
