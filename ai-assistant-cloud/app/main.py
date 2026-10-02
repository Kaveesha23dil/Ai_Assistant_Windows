"""The application.

Built by a factory so the test suite can construct one with fake services and a different
configuration without the real Gemini client, the real vector store, or a credential being
involved at all.
"""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api import agents, auth, chat, embeddings, vision
from app.config.settings import Settings, get_settings
from app.middleware.access_log import AccessLogMiddleware, configure_logging

logger = logging.getLogger(__name__)

DESCRIPTION = """
The cloud half of the Windows AI Assistant.

The desktop client holds no Gemini credential. It authenticates here, and this service is the
only thing that talks to Google. Requests that would leave the machine are the client's decision
to allow and this service's job to serve.

Images and prompts are processed in memory and are not written anywhere. Logs record that a
request happened, how long it took, and how many tokens it used, and nothing about what was asked.
"""


def create_app(settings: Settings | None = None) -> FastAPI:
    """Builds the application.

    Startup problems are logged as errors and the process still serves, because a health check
    that refuses to answer cannot tell Cloud Run that this revision is broken. The problem is
    reported by ``/health`` instead, which is the endpoint an operator will be looking at.
    """

    active = settings or get_settings()

    configure_logging(active.log_level)

    @asynccontextmanager
    async def lifespan(application: FastAPI):
        problems = active.validate_for_startup()

        if problems:
            for problem in problems:
                logger.error("Configuration problem: %s", problem)
        else:
            logger.info(
                "Starting in %s with the %s model and the %s vector store.",
                active.environment,
                active.gemini_model_name,
                "Vertex AI Vector Search" if active.vector_store == "vertex" else "in-process",
            )

        yield

        logger.info("Shutting down.")

    application = FastAPI(
        title="AI Assistant Cloud Backend",
        description=DESCRIPTION,
        version="1.0.0",
        lifespan=lifespan,
        # Docs are useful for the competition walkthrough and harmless: they describe the shape of
        # the API and none of its secrets. They are not disabled because hiding them would make
        # the service harder to demonstrate and would not hide anything.
        docs_url="/docs",
        redoc_url="/redoc",
    )

    # The desktop client is not a browser, so no origin is permitted by default. A wildcard here
    # would let any page on the internet call this API with a token it somehow obtained.
    application.add_middleware(
        CORSMiddleware,
        allow_origins=[],
        allow_credentials=False,
        allow_methods=["GET", "POST"],
        allow_headers=["Authorization", "Content-Type"],
    )

    application.add_middleware(AccessLogMiddleware)

    # The settings this application was built with, on the application state so that the
    # dependencies read the same object rather than the module-level cache. In a deployed process
    # they are identical; in a test they are not, and an auth decision made against the wrong
    # settings object is a decision about the wrong deployment.
    application.state.settings = active

    application.include_router(auth.router)
    application.include_router(chat.router)
    application.include_router(vision.router)
    application.include_router(embeddings.router)
    application.include_router(agents.router)

    @application.get("/", include_in_schema=False)
    async def root() -> dict[str, str]:
        """Says what this is and where the documentation is."""

        return {
            "service": "AI Assistant Cloud Backend",
            "docs": "/docs",
            "health": "/health",
        }

    return application


app = create_app()
