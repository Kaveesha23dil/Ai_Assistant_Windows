"""Token issuance, and the health check.

The token endpoint is refused in production. There is no account system here, so a token issued
from a caller-supplied subject would be a way for anyone who can reach the service to mint their
own credentials. In production the desktop client presents a token it obtained from Google's own
identity, and this endpoint does not exist.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException, status

from app.config.settings import Settings, get_settings
from app.middleware.dependencies import get_token_service
from app.middleware.security import AuthError, TokenService
from app.models.schemas import HealthResponse, TokenRequest, TokenResponse
from app.services.container import ServiceContainer, get_container

logger = logging.getLogger(__name__)

router = APIRouter(tags=["auth"])


@router.post("/api/auth/token", response_model=TokenResponse)
async def issue_token(
    request: TokenRequest,
    tokens: TokenService = Depends(get_token_service),
) -> TokenResponse:
    """Issues a short-lived bearer token.

    For local development and for the test suite only. Refused as soon as the deployment takes
    authentication seriously, rather than in production alone, because a token endpoint that trusts
    its caller is the same as no authentication and is harder to notice: every protected route would
    still answer, and the only sign would be this one route.

    In production the desktop client presents a token obtained from Google's own identity, and
    this endpoint does not exist.
    """

    settings = tokens.settings

    if settings.auth_is_protected or settings.is_production:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Not found.",
        )

    try:
        token, expires_in = tokens.issue(request.subject)
    except AuthError as error:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)
        ) from error

    return TokenResponse(access_token=token, expires_in=expires_in)


@router.get("/health", response_model=HealthResponse)
async def health(container: ServiceContainer = Depends(get_container)) -> HealthResponse:
    """Reports what this deployment looks like.

    Says plainly when authentication is off, when no credentials are present, and when there are
    configuration problems. Those are the three facts that determine whether the service is fit to
    face real traffic, and each is invisible from the outside otherwise.

    Never reports a key, a token, or a document count that could be used to infer what somebody
    indexed.
    """

    settings = container.settings
    problems = settings.validate_for_startup()

    if not settings.auth_is_protected:
        problems.append(
            "Authentication is not being enforced. Every request is reaching the model without a "
            "verified caller."
        )

    try:
        documents = await container.vectors.count()
    except Exception:  # noqa: BLE001
        # The health check must answer even when the store is unreachable, because a health check
        # that fails for the same reason as the thing it checks is useless during an incident.
        documents = 0
        problems.append("The vector store could not be reached.")

    status_value = "degraded" if problems else "ok"

    return HealthResponse(
        status=status_value,
        environment=settings.environment,
        model=settings.gemini_model_name,
        embedding_model=settings.gemini_embedding_model_name,
        authentication_required=settings.require_authentication,
        credentials_present=settings.has_gemini_credentials,
        using_vertex_ai=settings.uses_vertex_ai,
        vector_store=container.vectors.name,
        knowledge_documents=documents,
        problems=problems,
    )
