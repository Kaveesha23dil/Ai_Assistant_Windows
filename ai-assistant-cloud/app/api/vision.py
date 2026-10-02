"""Vision: one image, one answer, and no record of either.

The image arrives base64-encoded, is analysed, and is then dropped. Nothing here writes it to
disk, appends it to a log, or puts it in the response. The test suite asserts the absence of
those code paths rather than trusting the comment above.
"""

from __future__ import annotations

import base64
import binascii
import logging

from fastapi import APIRouter, Depends, HTTPException, status

from app.config.settings import Settings, get_settings
from app.middleware.dependencies import limit_requests
from app.models.schemas import VisionRequest, VisionResponse
from app.services.container import ServiceContainer, get_container
from app.services.gemini_service import GeminiUnavailableError

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/vision", tags=["vision"])

# The first bytes of each accepted format. Checked before the payload is decoded, so an
# executable or an archive renamed to .png is refused without the service parsing it.
_SIGNATURES: tuple[tuple[bytes, str], ...] = (
    (b"\x89PNG\r\n\x1a\n", "image/png"),
    (b"\xff\xd8\xff", "image/jpeg"),
    (b"GIF87a", "image/gif"),
    (b"GIF89a", "image/gif"),
)


@router.post("/analyze", response_model=VisionResponse)
async def analyze(
    request: VisionRequest,
    caller: str = Depends(limit_requests("vision", "rate_limit_vision_per_minute")),
    container: ServiceContainer = Depends(get_container),
    settings: Settings = Depends(get_settings),
) -> VisionResponse:
    """Answers a question about an image.

    The caller is the only place a screenshot is allowed to travel, and it has already agreed to
    let it leave the device: the Windows client checks ``AllowCloudScreenAnalysis`` before
    sending anything. This endpoint does not repeat that decision because it cannot make it.
    """

    mime_type, image_bytes = _decode(request.image_base64, settings.max_image_bytes)

    try:
        result = await container.gemini.analyze_image(
            image_bytes=image_bytes,
            mime_type=mime_type,
            question=request.question,
            instruction=request.analysis_type.instruction,
        )
    except GeminiUnavailableError as error:
        if error.code.startswith("IMAGE_"):
            # A problem with the picture is the caller's to fix, so it is a 400 rather than a 503.
            # Reporting it as a server error would send somebody looking at the deployment instead
            # of at the screenshot they sent.
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)
            ) from error

        logger.warning("A vision request could not reach the model: %s", error.code)
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="The AI service is not reachable right now.",
        ) from error

    # The decoded bytes go out of scope here and nothing holds a reference to them. That is the
    # whole of the deletion story: there is no store to delete from.
    del image_bytes

    if not result.text.strip():
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="The model returned an empty answer for this image.",
        )

    return VisionResponse(
        answer=result.text,
        analysis_type=request.analysis_type,
        model=result.model,
        tokens_used=result.tokens_used,
        latency_ms=result.latency_ms,
    )


def _decode(encoded: str, max_bytes: int) -> tuple[str, bytes]:
    """Decodes the payload and identifies the format from the bytes themselves.

    The declared media type is not trusted. A caller can label anything anything, and the type
    drives which decoder runs, so the type is read from the content instead. The declared type is
    checked against the sniffed one and a disagreement is a refusal rather than a preference.
    """

    cleaned = "".join(encoded.split())

    try:
        raw = base64.b64decode(cleaned, validate=True)
    except (binascii.Error, ValueError) as error:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="The image is not valid base64.",
        ) from error

    if not raw:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail="The image was empty."
        )

    # Checked before decoding the payload into pixels, so an oversized body costs one length check
    # rather than the memory to hold it.
    if len(raw) > max_bytes:
        raise HTTPException(
            status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
            detail=f"The image is larger than the {max_bytes} byte limit.",
        )

    for signature, mime_type in _SIGNATURES:
        if raw.startswith(signature):
            return mime_type, raw

    # WebP has no fixed four-byte signature, so it is identified from its container header.
    if raw[:4] == b"RIFF" and raw[8:12] == b"WEBP":
        return "image/webp", raw

    raise HTTPException(
        status_code=status.HTTP_400_BAD_REQUEST,
        detail="The image format is not one this service accepts.",
    )
