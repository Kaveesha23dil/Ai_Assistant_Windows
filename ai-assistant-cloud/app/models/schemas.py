"""Request and response contracts for the public API.

These are the only shapes a client is allowed to depend on. They are declared here rather than
inferred from a service so that a change to a service signature is visible as a change to the
API rather than arriving silently on a deployed endpoint.
"""

from __future__ import annotations

from enum import Enum
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    """The base every request and response model derives from.

    Wire format is camelCase and the Python names stay snake_case. Two reasons. A C# client
    deserializing into ``ConversationId`` matches ``conversationId`` without a naming policy on its
    side, and a Python developer reading the code sees names that match the field. Both spellings
    are accepted on input, because a client that sends snake_case and one that sends camelCase
    should both work rather than one of them failing on a name.
    """

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
    )


class AnalysisType(str, Enum):
    """What the caller wants done with an image.

    Declared rather than passed as free text so that a prompt built from an unrecognised value
    cannot be composed by accident. The value becomes part of the instruction sent to Gemini, so
    it is an input to a model call and is constrained here.
    """

    explain_error = "explain_error"
    describe_screenshot = "describe_screenshot"
    analyze_chart = "analyze_chart"
    read_text = "read_text"
    general = "general"

    @property
    def instruction(self) -> str:
        """The sentence added to the instruction for this kind of analysis."""

        return {
            AnalysisType.explain_error: (
                "Explain what error is shown, identify the likely cause, and give concrete steps "
                "to resolve it."
            ),
            AnalysisType.describe_screenshot: (
                "Describe what this screenshot shows, including the application and the state it "
                "appears to be in."
            ),
            AnalysisType.analyze_chart: (
                "Analyse this chart: describe the trend, note the notable values, and say what "
                "they suggest."
            ),
            AnalysisType.read_text: (
                "Transcribe the text visible in this image exactly as it appears."
            ),
            AnalysisType.general: "Describe what this image shows.",
        }[self]


class ChatRequest(ApiModel):
    """A question, optionally with the conversation it belongs to."""

    message: str = Field(min_length=1, max_length=32000, description="The question to answer.")
    conversation_id: str | None = Field(
        default=None, max_length=128, description="Groups related requests for the client."
    )
    use_knowledge: bool = Field(
        default=True, description="Whether to retrieve from the knowledge base before answering."
    )

    @field_validator("message")
    @classmethod
    def _message_is_not_blank(cls, value: str) -> str:
        # A whitespace-only message is not a question, and sending it costs a request and returns
        # an answer to nothing.
        stripped = value.strip()

        if not stripped:
            raise ValueError("message must not be blank.")

        return stripped


class Source(ApiModel):
    """One passage retrieved from the knowledge base."""

    title: str
    snippet: str
    score: float = Field(ge=0.0, le=1.0)
    document_id: str | None = None


class ChatResponse(ApiModel):
    """The answer, with what it was drawn from."""

    answer: str
    sources: list[Source] = Field(default_factory=list)
    tokens_used: int = Field(default=0, ge=0)
    conversation_id: str | None = None
    model: str | None = None
    latency_ms: int = Field(default=0, ge=0)


class StreamEvent(ApiModel):
    """One server-sent event in a streamed answer.

    The kind field is what lets the Windows client reuse the streaming path it already has: it
    reads a delta, text, or an error and does not have to distinguish a finished answer from a
    finished failure by looking at the payload.
    """

    kind: Literal["delta", "answer", "sources", "error", "done"] = "delta"
    text: str = ""
    sources: list[Source] = Field(default_factory=list)
    tokens_used: int = Field(default=0, ge=0)
    error_code: str | None = None


class VisionRequest(ApiModel):
    """An image and a question about it.

    The image arrives as base64 rather than as a URL so that the service never has to fetch a
    resource named by a caller, which would be a server-side request forgery waiting to happen
    and would put somebody else's image in the request path.
    """

    image_base64: str = Field(min_length=1, description="The image, base64 encoded.")
    question: str = Field(
        default="", max_length=4000, description="What to ask about the image. Optional."
    )
    analysis_type: AnalysisType = AnalysisType.general
    conversation_id: str | None = Field(default=None, max_length=128)

    @field_validator("question")
    @classmethod
    def _question_is_optional_but_not_blank(cls, value: str) -> str:
        return value.strip()


class VisionResponse(ApiModel):
    """The answer about an image.

    Deliberately carries no echo of the image. A response that repeated the request would put
    the person's screenshot into their own logs and into any proxy that recorded it.
    """

    answer: str
    analysis_type: AnalysisType
    model: str | None = None
    tokens_used: int = Field(default=0, ge=0)
    latency_ms: int = Field(default=0, ge=0)


class EmbeddingRequest(ApiModel):
    """One or more texts to embed."""

    texts: list[str] = Field(min_length=1, max_length=64, description="The texts to embed.")
    input_type: Literal["query", "document"] = "query"

    @field_validator("texts")
    @classmethod
    def _texts_are_not_blank(cls, value: list[str]) -> list[str]:
        cleaned = [text.strip() for text in value]

        if any(not text for text in cleaned):
            raise ValueError("texts must not contain blank entries.")

        return cleaned


class EmbeddingResponse(ApiModel):
    """The vectors, in the order the texts were given."""

    embeddings: list[list[float]]
    dimensions: int = Field(ge=1)
    model: str | None = None
    tokens_used: int = Field(default=0, ge=0)
    latency_ms: int = Field(default=0, ge=0)


class AgentRequest(ApiModel):
    """A task for the cloud agent to plan and carry out."""

    task: str = Field(min_length=1, max_length=8000, description="What to accomplish.")
    tools: list[str] = Field(
        default_factory=list,
        max_length=16,
        description="Which tools the agent may use. An empty list means the default set.",
    )
    conversation_id: str | None = Field(default=None, max_length=128)

    @field_validator("task")
    @classmethod
    def _task_is_not_blank(cls, value: str) -> str:
        stripped = value.strip()

        if not stripped:
            raise ValueError("task must not be blank.")

        return stripped

    @field_validator("tools")
    @classmethod
    def _tool_names_are_simple(cls, value: list[str]) -> list[str]:
        cleaned = [name.strip().lower() for name in value]

        # Tool names are used to look up handlers in a registry. Constraining them to a plain
        # identifier shape means a name can never be a path, a query, or an expression.
        for name in cleaned:
            if not name or not all(char.isalnum() or char in "._-" for char in name):
                raise ValueError(
                    "tool names must be simple identifiers made of letters, digits, dots, "
                    "underscores, or hyphens."
                )

        return cleaned


class AgentStep(ApiModel):
    """One step of a plan."""

    order: int = Field(ge=1)
    tool: str
    description: str
    arguments: dict[str, Any] = Field(default_factory=dict)
    status: Literal["planned", "completed", "failed", "skipped"] = "planned"
    output: str = ""


class AgentResponse(ApiModel):
    """The plan, what came of it, and how it ended."""

    plan: list[AgentStep] = Field(default_factory=list)
    result: str = ""
    status: Literal["completed", "failed", "partial"] = "completed"
    error_code: str | None = None
    model: str | None = None
    tokens_used: int = Field(default=0, ge=0)
    latency_ms: int = Field(default=0, ge=0)


class TokenRequest(ApiModel):
    """A request for a bearer token, used by the tests and by a first-run local setup.

    There is no sign-up and no password anywhere in this service. Identity is owned by the
    Windows application, which authenticates to Google and passes what it has been given; this
    endpoint exists so that a deployment can be exercised without a live desktop client, and it
    is refused in production.
    """

    subject: str = Field(default="local-developer", min_length=1, max_length=128)


class TokenResponse(ApiModel):
    """A signed bearer token."""

    access_token: str
    token_type: Literal["bearer"] = "bearer"
    expires_in: int = Field(ge=1)


class HealthResponse(ApiModel):
    """What the service reports about itself.

    Reports the shape of the deployment rather than the content of anything a person asked.
    Notably it never echoes a key, and it says plainly when authentication is off, because that
    is the condition an operator most needs to notice and least expects to be told about.
    """

    status: Literal["ok", "degraded"]
    environment: str
    model: str
    embedding_model: str
    authentication_required: bool
    credentials_present: bool
    using_vertex_ai: bool
    vector_store: str
    knowledge_documents: int = Field(default=0, ge=0)
    problems: list[str] = Field(default_factory=list)
    version: str = "1.0.0"
