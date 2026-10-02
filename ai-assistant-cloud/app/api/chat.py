"""Chat: one answer at a time, and the same answer streamed."""

from __future__ import annotations

import asyncio
import json
import logging
from collections.abc import AsyncIterator

from fastapi import APIRouter, Depends, HTTPException, status
from fastapi.responses import StreamingResponse

from app.middleware.dependencies import limit_requests, require_caller
from app.models.schemas import ChatRequest, ChatResponse, Source, StreamEvent
from app.services.container import ServiceContainer, get_container
from app.services.gemini_service import GeminiUnavailableError

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api", tags=["chat"])


@router.post("/chat", response_model=ChatResponse)
async def chat(
    request: ChatRequest,
    caller: str = Depends(limit_requests("chat", "rate_limit_chat_per_minute")),
    container: ServiceContainer = Depends(get_container),
) -> ChatResponse:
    """Answers a question.

    A retrieval failure does not become a failed request, so this endpoint answers even when the
    knowledge base is unreachable; the answer is then made without context and the model is told
    so, rather than the model inventing something.
    """

    try:
        result = await container.rag.answer(
            request.message, use_knowledge=request.use_knowledge
        )
    except GeminiUnavailableError as error:
        logger.warning("A chat request could not reach the model: %s", error.code)
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=(
                "The AI service is not reachable right now. The desktop client falls back to "
                "local capabilities in this case."
            ),
        ) from error

    if not result.answer.strip():
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="The model returned an empty answer.",
        )

    return ChatResponse(
        answer=result.answer,
        sources=result.sources,
        tokens_used=result.tokens_used,
        conversation_id=request.conversation_id,
        model=result.model,
        latency_ms=result.latency_ms,
    )


@router.post("/chat/stream")
async def chat_stream(
    request: ChatRequest,
    caller: str = Depends(limit_requests("chat", "rate_limit_chat_per_minute")),
    container: ServiceContainer = Depends(get_container),
) -> StreamingResponse:
    """Answers a question as server-sent events.

    The shape is one JSON object per event with a ``kind`` field, so the Windows client can read a
    delta, a set of sources, an error, or the end of the stream without inferring which it is
    from the payload. An error before the stream has started is a normal HTTP error, because a
    client that got a 401 cannot read the body of a stream it was never given; an error after it
    has started is an event, because by then the client has committed to the stream.
    """

    # Retrieval happens before the response starts, so that a failure is a status code rather than
    # an error event a client has to special-case.
    sources: list[Source] = []
    context = ""

    if request.use_knowledge:
        try:
            matches = await container.rag.retrieve(request.message)
            sources = [container.rag.to_source(match) for match in matches]
            context = container.rag.build_context(matches)
        except GeminiUnavailableError as error:
            logger.warning("Retrieval failed before streaming started: %s", error.code)

    return StreamingResponse(
        _stream(request, context, sources, container),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-store",
            "X-Accel-Buffering": "no",
        },
    )


async def _stream(
    request: ChatRequest,
    context: str,
    sources: list[Source],
    container: ServiceContainer,
) -> AsyncIterator[bytes]:
    """Yields the answer, then the sources, then a completion event.

    The sources go out after the text rather than before it, so a client can render an answer
    immediately and attach citations as they arrive instead of waiting for retrieval to finish
    before showing anything.
    """

    def frame(event: StreamEvent) -> bytes:
        return f"data: {event.model_dump_json()}\n\n".encode("utf-8")

    yield frame(StreamEvent(kind="sources", sources=sources))

    collected: list[str] = []
    tokens = 0

    try:
        async for delta in container.gemini.stream_text(
            request.message,
            system_instruction=container.rag.instruction_for(bool(context)),
            context=context or None,
        ):
            collected.append(delta)
            yield frame(StreamEvent(kind="delta", text=delta))
    except GeminiUnavailableError as error:
        # The stream has already begun, so this is an event rather than a status code.
        yield frame(
            StreamEvent(kind="error", error_code=error.code, text=str(error))
        )
        yield frame(StreamEvent(kind="done"))
        return
    except asyncio.CancelledError:
        # The client went away. Nothing to report and nothing to clean up beyond letting it
        # propagate, which is what stops the generator being closed twice.
        logger.info("A streamed answer was cancelled by the client.")
        raise

    tokens = container.gemini.last_tokens_used
    answer = "".join(collected)

    yield frame(StreamEvent(kind="answer", text=answer, tokens_used=tokens))
    yield frame(StreamEvent(kind="done", tokens_used=tokens))
