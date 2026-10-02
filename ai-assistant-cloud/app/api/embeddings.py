"""Embeddings: text in, vectors out."""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException, status

from app.middleware.dependencies import limit_requests
from app.models.schemas import EmbeddingRequest, EmbeddingResponse
from app.services.container import ServiceContainer, get_container
from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiUnavailableError

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api", tags=["embeddings"])


@router.post("/embeddings", response_model=EmbeddingResponse)
async def embed(
    request: EmbeddingRequest,
    caller: str = Depends(
        limit_requests("embeddings", "rate_limit_embeddings_per_minute")
    ),
    container: ServiceContainer = Depends(get_container),
) -> EmbeddingResponse:
    """Embeds a batch of texts.

    Bounded to 64 texts by the schema. A batch is one model call and one bill, so an unbounded one
    would let a single request cost more than the rest of the minute put together.
    """

    try:
        batch: EmbeddingService = await container.embeddings.embed_texts(
            request.texts, input_type=request.input_type
        )
    except GeminiUnavailableError as error:
        logger.warning("An embedding request could not reach the model: %s", error.code)
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="The embedding service is not reachable right now.",
        ) from error

    return EmbeddingResponse(
        embeddings=batch.embeddings,
        dimensions=batch.dimensions,
        model=batch.model,
        tokens_used=batch.tokens_used,
        latency_ms=batch.latency_ms,
    )
