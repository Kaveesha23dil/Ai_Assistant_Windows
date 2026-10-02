"""The one place that talks to Gemini.

Every route reaches the model through this service, and this service is the only module that
imports the Gemini SDK. That is the point of the file: a route that wanted to call the SDK
directly would have to reach past the timeout, the token ceiling, the input bound and the error
mapping, and the next thing that would happen is a route that logs a prompt.
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import AsyncIterator, Sequence
from dataclasses import dataclass, field
from typing import Any, Protocol

from app.config.settings import Settings, get_settings

logger = logging.getLogger(__name__)


class GeminiUnavailableError(RuntimeError):
    """Gemini could not be reached or refused the request.

    Carries a stable code so that a caller can decide what to tell a person without matching on
    a message, and so the Windows client can distinguish "the cloud is down" from "your prompt
    was refused" and fall back to local capabilities for the first and not the second.
    """

    def __init__(self, message: str, code: str = "GEMINI_UNAVAILABLE") -> None:
        super().__init__(message)
        self.code = code


@dataclass(slots=True)
class GeminiResult:
    """A completed answer and what it cost."""

    text: str
    model: str
    tokens_used: int = 0
    latency_ms: int = 0
    finish_reason: str = "stop"


@dataclass(slots=True)
class ContentPart:
    """One piece of a request: text, or an image.

    An image is held as bytes for the lifetime of the request and dropped when the call returns.
    It is never written to disk and never logged; see the vision route for why that is asserted by
    a test rather than merely intended here.
    """

    text: str | None = None
    image_bytes: bytes | None = None
    image_mime_type: str | None = None

    def __post_init__(self) -> None:
        if self.text is None and self.image_bytes is None:
            raise ValueError("A content part needs text or an image.")

        if self.image_bytes is not None and not self.image_mime_type:
            raise ValueError("An image part needs its media type.")


class GeminiClient(Protocol):
    """The narrow slice of the Gemini SDK this service uses.

    Declared so the service can be tested against a fake that returns a scripted answer, and so
    a future SDK rename is a compile error in one place rather than a runtime failure in four.
    """

    async def generate(self, **kwargs: Any) -> Any:
        """One non-streamed generation."""

    def stream_generate(self, **kwargs: Any) -> AsyncIterator[Any]:
        """One streamed generation."""


class _SdkGeminiClient:
    """The real client, built lazily so importing this module never needs a credential.

    Lazy construction matters for the test suite and for the health check: both import the
    application, and neither should fail because no key is configured. Whether a key is present
    is reported as a fact by the health check, not discovered as an import error.
    """

    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def _build(self) -> Any:
        if self._settings.uses_vertex_ai:
            try:
                from google import genai  # type: ignore[import-not-found]
            except ImportError as error:  # pragma: no cover - depends on the image
                raise GeminiUnavailableError(
                    "The Vertex AI client is not installed in this image.",
                    "GEMINI_SDK_MISSING",
                ) from error

            return genai.Client(
                vertexai=True,
                project=self._settings.project_id,
                location=self._settings.region,
            )

        try:
            from google import genai  # type: ignore[import-not-found]
        except ImportError as error:  # pragma: no cover - depends on the image
            raise GeminiUnavailableError(
                "The Gemini SDK is not installed in this image.",
                "GEMINI_SDK_MISSING",
            ) from error

        return genai.Client(api_key=self._settings.gemini_api_key)

    async def generate(self, **kwargs: Any) -> Any:
        client = self._build()

        return await client.aio.models.generate_content(**kwargs)

    async def stream_generate(self, **kwargs: Any) -> AsyncIterator[Any]:
        client = self._build()
        stream = await client.aio.models.generate_content_stream(**kwargs)

        async for chunk in stream:
            yield chunk


@dataclass(slots=True)
class _SessionState:
    """Per-request conversation turns, held only for the length of one call."""

    turns: list[dict[str, Any]] = field(default_factory=list)


class GeminiService:
    """Sends prompts to Gemini and returns answers, applying the configured limits on the way.

    The limits live here rather than at the call sites: a route that forgot to truncate would
    send an unbounded prompt, and a route that forgot the timeout would hold a request slot
    open. Both are things that happen once and are then invisible.
    """

    def __init__(
        self,
        settings: Settings | None = None,
        client: GeminiClient | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._client = client or _SdkGeminiClient(self._settings)
        self._last_tokens = 0

    @property
    def model_name(self) -> str:
        """The configured chat model."""

        return self._settings.gemini_model_name

    @property
    def is_configured(self) -> bool:
        """Whether a call could plausibly succeed, without making one."""

        return self._settings.has_gemini_credentials

    @property
    def last_tokens_used(self) -> int:
        """Tokens spent on the most recent call.

        Recorded on the service rather than returned by every method so that a tool can report
        its cost without every tool having to become the thing that unpacks a result. It is
        approximate in the sense that it belongs to the last call, not to a span of work, and the
        agent accumulates it per step rather than reading it once.
        """

        return self._last_tokens

    def _record_tokens(self, response: Any) -> int:
        """Reads and remembers the token count of one response."""

        self._last_tokens = self._read_tokens(response)

        return self._last_tokens

    async def generate_text(
        self,
        prompt: str,
        *,
        system_instruction: str | None = None,
        context: str | None = None,
        conversation: Sequence[dict[str, Any]] | None = None,
        temperature: float | None = None,
        max_tokens: int | None = None,
    ) -> GeminiResult:
        """Answers one prompt and returns the whole answer.

        ``context`` is retrieved material that goes in the prompt but not in the answer. It is
        separated from the question so that the model can be told, in the instruction, that the
        context is reference material and the question is what to answer.
        """

        prompt = self._bound(prompt)
        context = self._bound(context) if context else None

        started = time.perf_counter()
        contents = self._build_contents(prompt=prompt, context=context, conversation=conversation)

        try:
            response = await asyncio.wait_for(
                self._client.generate(
                    model=self._settings.gemini_model_name,
                    contents=contents,
                    config=self._config(system_instruction, temperature, max_tokens),
                ),
                timeout=self._settings.gemini_timeout_seconds,
            )
        except asyncio.TimeoutError as error:
            logger.warning("A Gemini request did not answer within the configured timeout.")
            raise GeminiUnavailableError(
                "The model did not answer in time.", "GEMINI_TIMEOUT"
            ) from error
        except GeminiUnavailableError:
            raise
        except Exception as error:  # noqa: BLE001 - the SDK raises a wide range of types
            # The vendor's message can contain the prompt, so it is not logged. The type is
            # enough to find the cause, and the person gets a sentence they can act on.
            logger.error(
                "A Gemini request failed with %s.", type(error).__name__
            )
            raise GeminiUnavailableError(
                "The model could not be reached.", "GEMINI_REQUEST_FAILED"
            ) from error

        text = self._read_text(response)

        return GeminiResult(
            text=text,
            model=self._settings.gemini_model_name,
            tokens_used=self._record_tokens(response),
            latency_ms=self._elapsed_ms(started),
            finish_reason=self._read_finish_reason(response),
        )

    async def stream_text(
        self,
        prompt: str,
        *,
        system_instruction: str | None = None,
        context: str | None = None,
        temperature: float | None = None,
        max_tokens: int | None = None,
    ) -> AsyncIterator[str]:
        """Yields the answer as it arrives.

        Yields text only. Token counts and finish reasons are not surfaced per chunk because they
        are properties of the whole response; the route assembles the summary event when the
        stream ends, which is the only moment at which it is known.
        """

        prompt = self._bound(prompt)
        context = self._bound(context) if context else None

        try:
            stream = self._client.stream_generate(
                model=self._settings.gemini_model_name,
                contents=self._build_contents(prompt=prompt, context=context, conversation=None),
                config=self._config(system_instruction, temperature, max_tokens),
            )

            async with asyncio.timeout(self._settings.gemini_timeout_seconds):
                async for chunk in stream:
                    text = self._read_text(chunk)

                    if text:
                        yield text
        except asyncio.TimeoutError as error:
            logger.warning("A streamed Gemini response did not finish within the timeout.")
            raise GeminiUnavailableError(
                "The model did not finish in time.", "GEMINI_TIMEOUT"
            ) from error
        except GeminiUnavailableError:
            raise
        except Exception as error:  # noqa: BLE001
            logger.error(
                "A streamed Gemini response failed with %s.", type(error).__name__
            )
            raise GeminiUnavailableError(
                "The model could not be reached.", "GEMINI_REQUEST_FAILED"
            ) from error

    async def analyze_image(
        self,
        image_bytes: bytes,
        mime_type: str,
        question: str,
        instruction: str,
    ) -> GeminiResult:
        """Answers a question about an image.

        The bytes are passed through as a part of the request and are not retained, copied to a
        file, or written to a log at any point in this method. When it returns, the caller's
        reference is the only one that matters and that is the caller's to drop.
        """

        if not image_bytes:
            raise GeminiUnavailableError("The image was empty.", "IMAGE_EMPTY")

        if len(image_bytes) > self._settings.max_image_bytes:
            raise GeminiUnavailableError(
                "The image is larger than this service accepts.",
                "IMAGE_TOO_LARGE",
            )

        if mime_type not in self._settings.allowed_image_mime_types:
            # Checked before the bytes are decoded or sent, so an unexpected format is refused
            # without the service doing any work with it.
            raise GeminiUnavailableError(
                "That image format is not accepted.", "IMAGE_TYPE_NOT_ALLOWED"
            )

        started = time.perf_counter()
        question = question.strip() or "Describe this image."

        contents = [
            ContentPart(text=instruction),
            ContentPart(text=question),
            ContentPart(image_bytes=image_bytes, image_mime_type=mime_type),
        ]

        try:
            response = await asyncio.wait_for(
                self._client.generate(
                    model=self._settings.gemini_model_name,
                    contents=self._to_contents(contents),
                    config=self._config(
                        system_instruction=(
                            "You are a careful assistant looking at a screenshot on someone's "
                            "screen. Answer only from what is visible. If something is not "
                            "legible, say so rather than guessing."
                        ),
                        # Vision uses the configured temperature and ceiling. It does not inherit
                        # anything from the caller's request settings, because a caller asking
                        # about an image has no way of knowing what that would mean for one.
                        temperature=None,
                        max_tokens=None,
                    ),
                ),
                timeout=self._settings.gemini_timeout_seconds,
            )
        except asyncio.TimeoutError as error:
            raise GeminiUnavailableError(
                "The model did not answer in time.", "GEMINI_TIMEOUT"
            ) from error
        except GeminiUnavailableError:
            raise
        except Exception as error:  # noqa: BLE001
            logger.error("A vision request failed with %s.", type(error).__name__)
            raise GeminiUnavailableError(
                "The model could not be reached.", "GEMINI_REQUEST_FAILED"
            ) from error

        return GeminiResult(
            text=self._read_text(response),
            model=self._settings.gemini_model_name,
            tokens_used=self._record_tokens(response),
            latency_ms=self._elapsed_ms(started),
        )

    async def count_tokens(self, text: str) -> int:
        """Approximates the token count of some text.

        The API is asked when it is reachable and falls back to a character heuristic when it is
        not. The number is reported for cost visibility only, so an estimate is better than a
        failed request, and the fallback is documented as an estimate at the call site.
        """

        try:
            response = await asyncio.wait_for(
                self._client.generate(
                    model=self._settings.gemini_model_name,
                    contents=self._to_contents([ContentPart(text=self._bound(text))]),
                ),
                timeout=self._settings.gemini_timeout_seconds,
            )
        except Exception:  # noqa: BLE001
            return self._approximate_tokens(text)

        return self._read_tokens(response)

    # ------------------------------------------------------------------ internals

    def _bound(self, text: str) -> str:
        """Applies the configured input ceiling.

        Truncated with a marker rather than refused. A refusal would mean a person who typed a
        long document got nothing back; a truncation means they get an answer about the part that
        fitted, and a visible marker in the prompt is what keeps the model honest about it.
        """

        limit = self._settings.gemini_max_input_characters
        cleaned = text.strip()

        if len(cleaned) <= limit:
            return cleaned

        logger.info(
            "Prompt truncated from %d to %d characters by the configured input bound.",
            len(cleaned),
            limit,
        )

        return cleaned[:limit] + "\n[truncated]"

    def _build_contents(
        self,
        *,
        prompt: str,
        context: str | None,
        conversation: Sequence[dict[str, Any]] | None,
    ) -> list[dict[str, Any]]:
        """Assembles the request in the order the model expects.

        Retrieved context is labelled inside the prompt rather than merged into it, so the model
        can tell the difference between what was retrieved and what was asked. Without that
        label a question like "what does this document say" can be answered from a passage that
        happens to contain the words rather than from the passage that answers it.
        """

        contents: list[dict[str, Any]] = [
            {"role": "user", "parts": [{"text": prompt}]}
        ]

        if context:
            contents.insert(
                0,
                {
                    "role": "user",
                    "parts": [
                        {
                            "text": (
                                "Reference material retrieved for this question. Use it where it "
                                "is relevant and say so when it is not:\n\n"
                                f"{context}"
                            )
                        }
                    ],
                },
            )

        if conversation:
            contents = [dict(turn) for turn in conversation] + contents

        return contents

    def _to_contents(self, parts: Sequence[ContentPart]) -> list[dict[str, Any]]:
        """Converts parts into the SDK's shape."""

        converted: list[dict[str, Any]] = []

        for part in parts:
            if part.text is not None:
                converted.append({"text": part.text})

            if part.image_bytes is not None:
                converted.append(
                    {
                        "inline_data": {
                            "mime_type": part.image_mime_type,
                            "data": part.image_bytes,
                        }
                    }
                )

        return converted

    def _config(
        self,
        system_instruction: str | None,
        temperature: float | None,
        max_tokens: int | None,
    ) -> dict[str, Any]:
        """The generation configuration, with the configured values as the defaults.

        Every value can be overridden per request, and every override is bounded by the same
        ceiling as the configuration. An override that asked for a hundred thousand tokens would
        cost a hundred thousand tokens.
        """

        ceiling = self._settings.gemini_max_tokens

        return {
            "temperature": (
                self._settings.gemini_temperature if temperature is None else temperature
            ),
            "max_output_tokens": min(max_tokens or ceiling, ceiling),
            "system_instruction": system_instruction or self._default_system_instruction(),
        }

    @staticmethod
    def _default_system_instruction() -> str:
        """The instruction used when a caller supplies none."""

        return (
            "You are the reasoning component of a Windows desktop assistant. Answer plainly and "
            "concisely. Prefer a specific answer over a general one. If you do not know, say so "
            "instead of producing something plausible. This is a desktop application: answers "
            "will be read on a screen, so keep them short unless asked for detail."
        )

    @staticmethod
    def _read_text(response: Any) -> str:
        """Pulls the text out of a response, whatever shape the SDK returned.

        The SDK has changed this accessor more than once, so each known shape is tried rather
        than assuming one. A response with no readable text becomes an empty string, which the
        route turns into a clear failure rather than a blank answer.
        """

        text = getattr(response, "text", None)

        if isinstance(text, str) and text:
            return text

        candidates = getattr(response, "candidates", None)

        if candidates:
            content = getattr(candidates[0], "content", None)
            parts = getattr(content, "parts", None)

            if parts:
                collected = [
                    getattr(part, "text", "")
                    for part in parts
                    if getattr(part, "text", None)
                ]

                if collected:
                    return "".join(collected)

        if isinstance(response, dict):
            if isinstance(response.get("text"), str):
                return response["text"]

            candidates = response.get("candidates")

            if candidates:
                parts = candidates[0].get("content", {}).get("parts", [])

                if parts:
                    return "".join(
                        part.get("text", "") for part in parts if part.get("text")
                    )

        return ""

    @staticmethod
    def _read_tokens(response: Any) -> int:
        """Reads the token usage, defaulting to zero when the SDK did not report it."""

        usage = getattr(response, "usage_metadata", None)

        if usage is None and isinstance(response, dict):
            usage = response.get("usage_metadata")

        total = getattr(usage, "total_token_count", None)

        if total is None and isinstance(usage, dict):
            total = usage.get("total_token_count")

        return int(total) if isinstance(total, int) else 0

    @staticmethod
    def _read_finish_reason(response: Any) -> str:
        """Reads why generation stopped, so a truncated answer can be reported as truncated."""

        candidates = getattr(response, "candidates", None)

        if not candidates:
            return "stop"

        reason = getattr(candidates[0], "finish_reason", None)

        if reason is None:
            return "stop"

        return str(getattr(reason, "name", None) or reason).lower()

    @staticmethod
    def _elapsed_ms(started: float) -> int:
        """Milliseconds since a start time, never negative."""

        return max(0, int((time.perf_counter() - started) * 1000))

    @staticmethod
    def _approximate_tokens(text: str) -> int:
        """A character-based estimate, used only when the real count is unavailable.

        Roughly four characters per token for English. It is an estimate and the caller says so;
        an honest approximation is more useful than a request that fails because a number was
        wanted for a log line.
        """

        return max(1, len(text) // 4)
