"""Application settings, read once from the environment and validated on startup.

Every value that changes behaviour arrives as an environment variable so that the image is
identical in every environment and a deployment differs only by configuration. Nothing here is
hardcoded: the model name, the token ceiling, the temperature and the timeout are all read, and
all four are validated here rather than discovered as a runtime error later.
"""

from __future__ import annotations

from functools import lru_cache
from typing import Literal

from pydantic import Field, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

Environment = Literal["development", "staging", "production"]


class Settings(BaseSettings):
    """Configuration for the whole service.

    A missing secret is an error at startup rather than a surprise at the first request. An
    operator who has deployed without a key finds out from the health check, not from a user's
    prompt failing.
    """

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        case_sensitive=False,
        # Both the environment variable name and the field name are accepted. The aliases are
        # what a deployment sets, and the field names are what a test passes; without this, a
        # value passed by field name is silently ignored and the default stands, which is the
        # worst possible failure for a configuration value.
        populate_by_name=True,
    )

    # ---------------------------------------------------------------- environment

    environment: Environment = "development"
    """Which environment this process believes it is running in. Drives logging verbosity."""

    log_level: str = "INFO"
    """Root log level. Content logging stays off regardless of this value."""

    # ---------------------------------------------------------------- Gemini

    gemini_api_key: str = Field(default="", alias="GEMINI_API_KEY")
    """The Gemini credential. Never logged, never returned in a response, never sent to a client.

    In production this is expected to be absent from the environment entirely, in which case the
    service authenticates to Vertex AI with Application Default Credentials. That is the correct
    configuration and it is why an empty key is allowed here but not in production use.
    """

    gemini_model_name: str = Field(default="gemini-2.0-flash", alias="MODEL_NAME")
    """The chat and vision model. Named by configuration so it can be changed without a rebuild."""

    gemini_embedding_model_name: str = Field(
        default="text-embedding-004", alias="EMBEDDING_MODEL_NAME"
    )
    """The embedding model. Separate from the chat model because they are replaced on different
    schedules and a chat model upgrade should not silently re-embed an entire knowledge base."""

    gemini_max_tokens: int = Field(default=2048, ge=1, le=32768, alias="MAX_TOKENS")
    """Ceiling on the answer length."""

    gemini_temperature: float = Field(default=0.4, ge=0.0, le=2.0, alias="TEMPERATURE")
    """Low by default. A summarisation answer that varies run to run is harder to trust."""

    gemini_timeout_seconds: float = Field(default=60.0, gt=0, le=600, alias="TIMEOUT")
    """Per-request ceiling. A streaming answer that has not begun by now will not be waited for."""

    gemini_max_input_characters: int = Field(
        default=32000, ge=1, alias="MAX_INPUT_CHARACTERS"
    )
    """How much prompt text is accepted before truncation. Bounds the request body and the cost
    of one request."""

    # ---------------------------------------------------------------- Google Cloud

    project_id: str = Field(default="", alias="PROJECT_ID")
    """The Google Cloud project. Required for Vertex AI calls and for anything that needs a
    resource path."""

    region: str = Field(default="us-central1", alias="REGION")
    """Deployment region for Cloud Run and the default location for Vertex AI resources."""

    # ---------------------------------------------------------------- security

    jwt_secret: str = Field(default="", alias="JWT_SECRET")
    """Signing secret for bearer tokens. Required in production; a generated development value is
    used when unset so the service runs locally without setup."""

    jwt_algorithm: str = Field(default="HS256", alias="JWT_ALGORITHM")
    jwt_access_token_minutes: int = Field(default=60, ge=1, le=1440, alias="JWT_ACCESS_TOKEN_MINUTES")
    jwt_issuer: str = Field(default="ai-assistant-cloud", alias="JWT_ISSUER")

    require_authentication: bool = Field(default=True, alias="REQUIRE_AUTHENTICATION")
    """Turned off only for the test suite and for a local run against a mock model. A deployment
    that leaves it off is open to anybody who can reach the URL, and the health check reports it
    so that is visible rather than silent."""

    # ---------------------------------------------------------------- rate limiting

    rate_limit_chat_per_minute: int = Field(default=30, ge=1, alias="RATE_LIMIT_CHAT_PER_MINUTE")
    rate_limit_vision_per_minute: int = Field(default=10, ge=1, alias="RATE_LIMIT_VISION_PER_MINUTE")
    rate_limit_embeddings_per_minute: int = Field(default=120, ge=1, alias="RATE_LIMIT_EMBEDDINGS_PER_MINUTE")
    rate_limit_agent_per_minute: int = Field(default=10, ge=1, alias="RATE_LIMIT_AGENT_PER_MINUTE")

    # ---------------------------------------------------------------- vision privacy

    max_image_bytes: int = Field(default=5 * 1024 * 1024, ge=1024, alias="MAX_IMAGE_BYTES")
    """Largest image accepted. Bounded before decode so an oversized body cannot exhaust memory."""

    allowed_image_mime_types: tuple[str, ...] = (
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
    )
    """Formats accepted for analysis. Anything else is refused before it is decoded, so an
    unexpected format is not a route to having arbitrary bytes parsed."""

    # ---------------------------------------------------------------- vector store

    vector_store: Literal["in-memory", "vertex"] = Field(
        default="in-memory", alias="VECTOR_STORE"
    )
    """Which store to use.

    Explicit rather than inferred from the presence of a project id, because the project id is also
    what the model calls need and inferring from it would turn on a vector store nobody asked for.
    """

    vector_search_index_id: str = Field(default="", alias="VECTOR_SEARCH_INDEX_ID")
    """The Vertex AI Vector Search index to read and write.

    Required when ``VECTOR_STORE`` is ``vertex``, and reported by the health check as a problem
    when it is missing.
    """

    vector_search_endpoint: str = Field(default="", alias="VECTOR_SEARCH_ENDPOINT")
    """Overrides the regional endpoint, for a private endpoint or a test double.

    Empty means ``https://<region>-aiplatform.googleapis.com``.
    """

    # ---------------------------------------------------------------- knowledge

    knowledge_collection: str = Field(default="assistant-knowledge", alias="KNOWLEDGE_COLLECTION")
    """A human-readable name for the knowledge base, reported by the health check.

    Not an index id. The index is named by ``VECTOR_SEARCH_INDEX_ID``.
    """

    rag_top_k: int = Field(default=8, ge=1, le=50, alias="RAG_TOP_K")
    rag_max_context_characters: int = Field(default=30000, ge=1, alias="RAG_MAX_CONTEXT_CHARACTERS")
    """Ceiling on the retrieved context. More context than the model will read makes retrieval
    worse rather than better."""

    rag_minimum_similarity: float = Field(default=0.3, ge=0.0, le=1.0, alias="RAG_MINIMUM_SIMILARITY")
    rag_max_passages_per_document: int = Field(
        default=2000,
        ge=1,
        alias="RAG_MAX_PASSAGES_PER_DOCUMENT",
    )
    """How many passages a single document may contribute.

    Removal works by deriving a document's passage ids from its title, so it needs an upper bound
    on how many there can be. Generous enough that no real document reaches it, and small enough
    that removing one document does not send a thousand ids on every deletion.
    """
    """Below this the chunks are treated as not relevant, and the model is told there is no
    context instead of being handed noise."""

    # ---------------------------------------------------------------- logging privacy

    log_request_bodies: bool = Field(default=False, alias="LOG_REQUEST_BODIES")
    """Deliberately off and never turned on by a configuration file. Prompts are private by
    default; this exists so a developer can opt in locally and see what is happening."""

    # ---------------------------------------------------------------- validators

    @field_validator("log_level")
    @classmethod
    def _upper_log_level(cls, value: str) -> str:
        level = value.strip().upper()

        if level not in {"CRITICAL", "ERROR", "WARNING", "INFO", "DEBUG"}:
            raise ValueError("LOG_LEVEL must be a standard Python logging level.")

        return level

    @field_validator("gemini_api_key")
    @classmethod
    def _strip_key(cls, value: str) -> str:
        # A key that arrived with surrounding whitespace from a secret manager is a common and
        # confusing failure, because the error from the vendor is an authentication error.
        return value.strip()

    @property
    def is_production(self) -> bool:
        """Whether this process is serving real users."""

        return self.environment == "production"

    @property
    def uses_vertex_ai(self) -> bool:
        """Whether to call Vertex AI with Application Default Credentials.

        True when there is no API key and a project is named. This is the configuration where no
        long-lived secret exists anywhere, which is the one a production deployment should use.
        """

        return not self.gemini_api_key and bool(self.project_id)

    @property
    def has_gemini_credentials(self) -> bool:
        """Whether a call to Gemini could plausibly succeed.

        The health check reports this. It is deliberately a statement about credentials rather
        than a live call, so that a health check does not cost a request or bill a token.
        """

        return bool(self.gemini_api_key) or bool(self.project_id)

    @property
    def auth_is_protected(self) -> bool:
        """Whether a bearer token is actually being required.

        Reported by the health check, because a deployment with authentication off is the single
        most important thing to notice and it is otherwise invisible.
        """

        return self.require_authentication and bool(self.jwt_secret)

    def validate_for_startup(self) -> list[str]:
        """Returns the problems that should stop this process from starting.

        A missing signing secret in production is a refusal rather than a warning: without it
        every token would be signed with a value from the source, which is the same as no
        authentication at all while appearing to have it.
        """

        problems: list[str] = []

        if self.is_production:
            if not self.jwt_secret:
                problems.append(
                    "JWT_SECRET must be set in production. Without it every token would be "
                    "signed with a value from the source code."
                )

            if len(self.jwt_secret) < 32:
                problems.append(
                    "JWT_SECRET must be at least 32 characters in production. A short signing "
                    "secret is brute-forceable."
                )

            if not self.has_gemini_credentials:
                problems.append(
                    "Either GEMINI_API_KEY or PROJECT_ID must be set. The service has no way to "
                    "reach Gemini."
                )

            if self.log_request_bodies:
                problems.append(
                    "LOG_REQUEST_BODIES must not be true in production. Request bodies contain "
                    "the prompts and images people asked about."
                )

        if self.require_authentication and not self.jwt_secret:
            problems.append(
                "REQUIRE_AUTHENTICATION is on but JWT_SECRET is empty, so no token could be "
                "validated. Set JWT_SECRET or turn authentication off deliberately."
            )

        if self.vector_store == "vertex" and not self.vector_search_index_id:
            # Not fatal, but it means every answer is given without the knowledge base while the
            # health check says the store is Vertex, which is the worst of both.
            problems.append(
                "VECTOR_STORE is 'vertex' but VECTOR_SEARCH_INDEX_ID is empty. The service fell "
                "back to the in-process store, so nothing is indexed and no answer is grounded."
            )

        if self.vector_store == "vertex" and not self.project_id:
            problems.append(
                "VECTOR_STORE is 'vertex' but PROJECT_ID is empty, so there is no project to read "
                "the index from."
            )

        return problems


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """The settings for this process.

    Cached so that the whole application reads the same values, including the same generated
    development secret. A service that signed with a different secret per call would reject its
    own tokens.
    """

    return Settings()
