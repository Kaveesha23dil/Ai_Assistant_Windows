"""Retrieval-augmented answering.

Question, embed, search, then answer with the retrieved passages as reference material. The
service owns the decision about what counts as relevant, and the routes never see a vector.
"""

from __future__ import annotations

import logging
import time
from dataclasses import dataclass, field
from typing import Any

from app.config.settings import Settings, get_settings
from app.models.schemas import Source
from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiResult, GeminiService, GeminiUnavailableError
from app.services.vector_database import (
    InMemoryVectorDatabase,
    IVectorDatabase,
    SearchMatch,
    VectorRecord,
)

logger = logging.getLogger(__name__)


@dataclass(slots=True)
class RagAnswer:
    """An answer, what it was drawn from, and how it went."""

    answer: str
    sources: list[Source] = field(default_factory=list)
    tokens_used: int = 0
    latency_ms: int = 0
    model: str | None = None
    used_knowledge: bool = False


class RAGService:
    """Answers questions using the knowledge base."""

    def __init__(
        self,
        settings: Settings | None = None,
        gemini: GeminiService | None = None,
        embeddings: EmbeddingService | None = None,
        vectors: IVectorDatabase | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._gemini = gemini or GeminiService(self._settings)
        self._embeddings = embeddings or EmbeddingService(self._settings)
        # An in-process store when none was passed, so a service built by hand is runnable rather
        # than failing at the first query with "the store has not been attached". The container
        # passes the configured store, so this default is what local use and tests see.
        self._vectors = vectors or InMemoryVectorDatabase()
        self._last_tokens = 0

    @property
    def vectors(self) -> IVectorDatabase:
        """The store in use."""

        if self._vectors is None:
            # Reachable only if a caller assigned None after construction, which nothing in the
            # application does. Said out loud rather than returning an empty answer, because an
            # empty answer to a question about a document reads as "not found" when it means
            # "nothing was configured".
            raise RuntimeError("The vector store has not been attached to this service.")

        return self._vectors

    async def index(self, passages: list[tuple[str, str]]) -> int:
        """Embeds and stores passages.

        Each passage is a title and its text. Returns how many were written, so a caller can tell
        the difference between "stored nothing" and "stored nothing because there was nothing".

        The id is derived from the title and the passage's position within the document rather than
        from the position within the batch, so indexing a document again replaces its own chunks
        instead of adding a second copy that would then be cited twice as if it were two sources.
        """

        if not passages:
            return 0

        kept = [(title, text) for title, text in passages if text and text.strip()]

        if not kept:
            return 0

        texts = [text for _, text in kept]

        batch = await self._embeddings.embed_texts(texts, input_type="document")

        records = [
            VectorRecord(
                id=self.passage_id(title, position),
                text=text,
                vector=vector,
                metadata={"title": title, "source": title},
            )
            for position, ((title, text), vector) in enumerate(
                zip(kept, batch.embeddings, strict=True)
            )
        ]

        written = await self.vectors.upsert(records)

        logger.info("Indexed %d passage(s) into %s.", written, self.vectors.name)

        return written

    async def remove_document(self, title: str) -> int:
        """Removes every passage of a document and reports how many were removed.

        Ids are derived from the title and the passage position, so the passages are addressable
        without first listing what the store holds. That matters for a person who has just deleted
        a file and expects the assistant to stop quoting it.
        """

        removed = await self.vectors.delete(
            [
                self.passage_id(title, position)
                for position in range(self._settings.rag_max_passages_per_document)
            ]
        )

        logger.info("Removed %d passage(s) for %r.", removed, title)

        return removed

    @classmethod
    def passage_id(cls, title: str, position: int) -> str:
        """The store id for one passage of a document.

        Public because a client holding a document id from a citation needs to be able to work out
        what to remove.
        """

        return f"{cls._slugify(title)}-{position}"

    async def answer(
        self,
        question: str,
        *,
        use_knowledge: bool = True,
        conversation: list[dict[str, Any]] | None = None,
    ) -> RagAnswer:
        """Answers a question, retrieving first when the knowledge base is enabled.

        A retrieval failure does not fail the question. Someone asking what the time is should get
        an answer even when the knowledge base is unreachable, and the answer is better without
        context than no answer at all.
        """

        started = time.perf_counter()
        context = ""
        sources: list[Source] = []

        if use_knowledge:
            try:
                matches = await self.retrieve(question)
                sources = [self.to_source(match) for match in matches]
                context = self.build_context(matches)
            except GeminiUnavailableError as error:
                logger.warning(
                    "Retrieval failed, answering without knowledge base context (%s).", error.code
                )

        result = await self._gemini.generate_text(
            question,
            system_instruction=self.instruction_for(bool(context)),
            context=context or None,
            conversation=conversation,
        )

        return RagAnswer(
            answer=result.text,
            sources=sources,
            tokens_used=result.tokens_used,
            latency_ms=max(0, int((time.perf_counter() - started) * 1000)),
            model=result.model,
            used_knowledge=bool(context),
        )

    async def retrieve(self, question: str, top_k: int | None = None) -> list[SearchMatch]:
        """Finds the passages most relevant to a question.

        Returns nothing when nothing clears the similarity floor. Handing the model passages that
        are merely the least irrelevant in the store is worse than telling it there is no
        context, because it will answer from them confidently.
        """

        limit = top_k or self._settings.rag_top_k
        query_vector = await self._embeddings.embed_query(question)

        matches = await self.vectors.search(
            query_vector,
            top_k=limit,
            minimum_score=self._settings.rag_minimum_similarity,
        )

        # A retrieval is an embedding call as well as a search, and both cost. Reported so an
        # agent run that searched the knowledge base twenty times can say so.
        self._last_tokens = self._embeddings.last_tokens_used

        return matches

    @property
    def last_tokens_used(self) -> int:
        """Tokens spent by the most recent retrieval.

        Kept on the service so a tool can report the cost of a search without the tool having to
        know that a search involves a model call.
        """

        return self._last_tokens

    async def document_count(self) -> int:
        """How many passages are indexed."""

        return await self.vectors.count()

    # ------------------------------------------------------------------ internals

    def build_context(self, matches: list[SearchMatch]) -> str:
        """Assembles the retrieved passages into labelled reference material.

        Each passage is numbered and titled, and the whole block is capped. A context block
        larger than the model's window would have its tail silently ignored, which looks exactly
        like the tail not having been retrieved.
        """

        if not matches:
            return ""

        budget = self._settings.rag_max_context_characters
        blocks: list[str] = []
        used = 0

        for index, match in enumerate(matches, start=1):
            title = match.record.metadata.get("title", "Untitled")
            block = f"[{index}] {title}\n{match.record.text}"

            if used + len(block) > budget:
                if used == 0:
                    # One oversized passage still gets through, truncated, rather than the
                    # question being answered with no context at all.
                    blocks.append(block[:budget])
                break

            blocks.append(block)
            used += len(block)

        return "\n\n".join(blocks)

    def instruction_for(self, has_context: bool) -> str:
        """The instruction, which differs depending on whether anything was retrieved.

        Public because the streaming route builds its own context and must give the model the same
        instruction the non-streaming path would.

        This is the difference between an assistant that cites what it found and one that
        confabulates a citation. With no context the instruction says so explicitly, so the
        model's answer to "what does our policy say" is "I don't know" rather than an invention
        with a confident tone.
        """

        if has_context:
            return (
                "Answer the question using the reference material provided. Cite the passage "
                "numbers you relied on, like [2], so the person can check them. If the material "
                "does not answer the question, say that it does not rather than filling the gap "
                "from general knowledge."
            )

        return (
            "Answer the question directly. Nothing was found in the knowledge base for this "
            "question, so if the answer would have to come from a document you were not given, "
            "say that plainly instead of guessing what it says."
        )

    def to_source(self, match: SearchMatch) -> Source:
        """Turns a match into the source shown beside an answer.

        Public because the streaming route assembles its own context and needs the same
        labelling. Having one place that decides what a source looks like is worth more than
        keeping it private.
        """

        return Source(
            title=str(match.record.metadata.get("title", "Untitled")),
            snippet=match.record.text[:400],
            score=round(match.score, 4),
            document_id=match.record.id,
        )

    @staticmethod
    def _slugify(value: str) -> str:
        """A short identifier fragment derived from a title."""

        cleaned = "".join(char if char.isalnum() else "-" for char in value.lower())

        while "--" in cleaned:
            cleaned = cleaned.replace("--", "-")

        return cleaned.strip("-")[:60] or "passage"


def build_answer(response: GeminiResult) -> RagAnswer:
    """Wraps a bare model answer with no sources, for the paths that bypass retrieval."""

    return RagAnswer(
        answer=response.text,
        tokens_used=response.tokens_used,
        latency_ms=response.latency_ms,
        model=response.model,
    )
