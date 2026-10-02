"""The Gemini service: what it bounds, what it reports, and what it refuses.

Every test here runs against the fake client. The assertions are about behaviour that would be
invisible if the model happened to answer well, which is the entire class of bug a language model
integration is prone to.
"""

from __future__ import annotations

import asyncio

import pytest

from app.services.gemini_service import GeminiService, GeminiUnavailableError
from tests.fakes import FakeGeminiClient, make_gemini, make_settings


async def test_a_prompt_reaches_the_configured_model() -> None:
    """The model name comes from configuration rather than from the call site."""

    settings = make_settings(gemini_model_name="gemini-2.5-pro")
    client = FakeGeminiClient(["An answer."])
    service = GeminiService(settings, client)

    result = await service.generate_text("A question.")

    assert client.calls[0]["model"] == "gemini-2.5-pro"
    assert result.model == "gemini-2.5-pro"
    assert result.text == "An answer."


async def test_the_configured_ceiling_is_applied_when_the_caller_asks_for_nothing() -> None:
    """A caller that sets no token ceiling gets the configured one."""

    settings = make_settings(gemini_max_tokens=999)
    client = FakeGeminiClient(["An answer."])

    await GeminiService(settings, client).generate_text("A question.")

    assert client.calls[0]["config"]["max_output_tokens"] == 999


async def test_a_call_cannot_ask_for_more_tokens_than_the_ceiling() -> None:
    """An override above the ceiling is clamped.

    A per-request override exists so a caller can ask for a shorter answer. Without the clamp it
    would also be a way for a request to ask for a hundred thousand tokens, and the setting that
    was meant to bound cost would stop bounding anything.
    """

    settings = make_settings(gemini_max_tokens=500)
    client = FakeGeminiClient(["An answer."])

    await GeminiService(settings, client).generate_text("A question.", max_tokens=100_000)

    assert client.calls[0]["config"]["max_output_tokens"] == 500


async def test_a_call_may_ask_for_fewer_tokens_than_the_ceiling() -> None:
    """An override below the ceiling is honoured."""

    settings = make_settings(gemini_max_tokens=500)
    client = FakeGeminiClient(["An answer."])

    await GeminiService(settings, client).generate_text("A question.", max_tokens=50)

    assert client.calls[0]["config"]["max_output_tokens"] == 50


async def test_an_oversized_prompt_is_truncated_rather_than_refused() -> None:
    """A prompt over the input bound is shortened, with a marker.

    A refusal would mean somebody who pasted a long document got nothing back. The marker matters:
    without it the model answers as if it had seen the whole thing.
    """

    settings = make_settings(gemini_max_input_characters=100)
    client = FakeGeminiClient(["An answer."])

    await GeminiService(settings, client).generate_text("word " * 200)

    sent = str(client.calls[0]["contents"])
    assert "[truncated]" in sent
    assert len(sent) < 400


async def test_a_prompt_within_the_bound_is_sent_whole() -> None:
    """Nothing is cut from a prompt that fits."""

    settings = make_settings(gemini_max_input_characters=1000)
    client = FakeGeminiClient(["An answer."])

    await GeminiService(settings, client).generate_text("A short question.")

    assert "[truncated]" not in str(client.calls[0]["contents"])


async def test_retrieved_context_is_labelled_so_the_model_can_tell_it_from_the_question() -> None:
    """Context goes in as reference material, separate from the question."""

    client = FakeGeminiClient(["An answer."])

    await GeminiService(make_settings(), client).generate_text(
        "What does it say?", context="The policy says refunds take 30 days."
    )

    contents = client.calls[0]["contents"]

    # Two turns, context first. The model can answer "what does this document say" from a
    # labelled reference block and decline to answer from a question that happens to contain the
    # same words.
    assert len(contents) == 2
    assert "Reference material" in str(contents[0])
    assert str(contents[-1]).find("What does it say?") >= 0


async def test_no_reference_block_is_added_when_there_is_no_context() -> None:
    """A question with nothing retrieved is sent alone."""

    client = FakeGeminiClient(["An answer."])

    await GeminiService(make_settings(), client).generate_text("What time is it?")

    assert len(client.calls[0]["contents"]) == 1


async def test_a_slow_model_times_out_with_a_code_the_client_can_act_on() -> None:
    """A request that outlasts the timeout raises rather than hanging.

    The code matters more than the exception: it is what lets the desktop client decide to fall
    back to local capabilities instead of showing an error.
    """

    settings = make_settings(gemini_timeout_seconds=0.05)
    client = FakeGeminiClient(["An answer."], delay=0.5)

    with pytest.raises(GeminiUnavailableError) as failure:
        await GeminiService(settings, client).generate_text("A question.")

    assert failure.value.code == "GEMINI_TIMEOUT"


async def test_a_vendor_error_becomes_a_stable_code_and_no_vendor_text() -> None:
    """The vendor's message is not passed on.

    SDK errors routinely quote the prompt back. Passing that to a caller would put somebody's
    document into an error response that may be logged by something else.
    """

    class VendorError(RuntimeError):
        """Stands in for an SDK error carrying the prompt."""

    client = FakeGeminiClient(fail_with=VendorError("400 for input: my private document"))

    with pytest.raises(GeminiUnavailableError) as failure:
        await GeminiService(make_settings(), client).generate_text("my private document")

    assert failure.value.code == "GEMINI_REQUEST_FAILED"
    assert "private document" not in str(failure.value)


async def test_a_model_that_says_nothing_yields_an_empty_answer_rather_than_an_error() -> None:
    """An empty response is passed back as empty.

    The route turns that into a clear failure. Turning it into an exception here would mean a
    refusal from the model's own safety system looked like an outage.
    """

    service = make_gemini(replies=[""])

    result = await service.generate_text("A question.")

    assert result.text == ""


async def test_streaming_yields_the_answer_in_pieces() -> None:
    """A streamed answer arrives as several deltas."""

    client = FakeGeminiClient(["one two three"])

    pieces = [
        piece
        async for piece in GeminiService(make_settings(), client).stream_text("A question.")
    ]

    assert len(pieces) > 1
    assert "".join(pieces).strip() == "one two three"


async def test_a_stream_that_fails_after_it_started_raises_to_the_stream() -> None:
    """A mid-stream failure surfaces, so the route can report it.

    Swallowing it would leave the client with a truncated answer and no indication that anything
    went wrong, which is the one outcome that is worse than an error.
    """

    class VendorError(RuntimeError):
        """Stands in for a mid-stream failure."""

    client = FakeGeminiClient(fail_with=VendorError("connection reset"))

    with pytest.raises(GeminiUnavailableError):
        async for _ in GeminiService(make_settings(), client).stream_text("A question."):
            pass


async def test_token_usage_is_reported() -> None:
    """A caller can see what a request cost."""

    client = FakeGeminiClient(["An answer."], tokens=123)

    result = await GeminiService(make_settings(), client).generate_text("A question.")

    assert result.tokens_used == 123


async def test_the_service_reports_whether_it_is_configured_without_calling_anything() -> None:
    """Credential presence is a fact the health check can read.

    It must not cost a request: a health check that called the model would be billed every time
    Cloud Run polled it.
    """

    assert GeminiService(make_settings(gemini_api_key="")).is_configured is False
    assert GeminiService(make_settings(gemini_api_key="key")).is_configured is True
    assert GeminiService(make_settings(project_id="a-project")).is_configured is True


async def test_a_configured_project_means_vertex_ai_rather_than_a_key() -> None:
    """A project with no key selects Application Default Credentials.

    This is the production shape, and it is the one with no long-lived secret anywhere.
    """

    settings = make_settings(gemini_api_key="", project_id="a-project")

    assert settings.uses_vertex_ai is True


async def test_the_image_bound_is_checked_before_the_image_is_sent() -> None:
    """An oversized image is refused without a model call."""

    client = FakeGeminiClient(["An answer."])
    settings = make_settings(max_image_bytes=1024)

    with pytest.raises(GeminiUnavailableError) as failure:
        await GeminiService(settings, client).analyze_image(
            image_bytes=b"x" * 2000,
            mime_type="image/png",
            question="What is this?",
            instruction="Describe.",
        )

    assert failure.value.code == "IMAGE_TOO_LARGE"
    assert client.calls == []


async def test_an_unexpected_image_format_is_refused() -> None:
    """A format outside the accepted list never reaches the model."""

    client = FakeGeminiClient(["An answer."])

    with pytest.raises(GeminiUnavailableError) as failure:
        await GeminiService(make_settings(), client).analyze_image(
            image_bytes=b"%PDF-1.4",
            mime_type="application/pdf",
            question="What is this?",
            instruction="Describe.",
        )

    assert failure.value.code == "IMAGE_TYPE_NOT_ALLOWED"
    assert client.calls == []


async def test_an_accepted_image_reaches_the_model_and_comes_back_as_an_answer() -> None:
    """The happy path for an image."""

    client = FakeGeminiClient(["A dialog box."])

    result = await GeminiService(make_settings(), client).analyze_image(
        image_bytes=b"\x89PNG\r\n\x1a\n" + b"0" * 50,
        mime_type="image/png",
        question="What is this?",
        instruction="Describe.",
    )

    assert result.text == "A dialog box."
    assert client.calls[0]["contents"]


async def test_a_vision_request_carries_an_instruction_about_the_screenshot() -> None:
    """The image path has its own instruction rather than the general one.

    A screenshot and a chat question are different tasks: one asks what is on a screen, the other
    asks something in general. Using the chat instruction for an image is how a screenshot comes
    back described as though it were a document.
    """

    client = FakeGeminiClient(["An answer."])

    await GeminiService(make_settings(), client).analyze_image(
        image_bytes=b"\x89PNG\r\n\x1a\n" + b"0" * 50,
        mime_type="image/png",
        question="What is this?",
        instruction="Describe.",
    )

    instruction = client.calls[0]["config"]["system_instruction"]

    assert "screenshot" in instruction.lower()


async def test_the_token_estimate_falls_back_rather_than_failing() -> None:
    """An unreachable counting endpoint still gives a number.

    The number is for a log line, so an estimate is better than a failed request, and the
    estimate is labelled as one at the call site.
    """

    class Failing:
        """A client that always fails."""

        async def generate(self, **kwargs):
            """Fails."""
            raise RuntimeError("unavailable")

        async def stream_generate(self, **kwargs):
            """Fails."""
            raise RuntimeError("unavailable")

    service = GeminiService(make_settings(), Failing())

    assert await service.count_tokens("a" * 400) > 0
