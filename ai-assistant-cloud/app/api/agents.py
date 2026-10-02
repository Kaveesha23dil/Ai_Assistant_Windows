"""The agent: run a task, and report what actually happened.

A partial run is reported as partial. A failed step is not hidden behind a summary that reads as
if everything worked, because the whole value of asking an agent to do something is that its
report of what it did can be believed.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, Depends, HTTPException, status

from app.middleware.dependencies import limit_requests
from app.models.schemas import AgentRequest, AgentResponse, AgentStep
from app.services.agent_service import AgentError
from app.services.container import ServiceContainer, get_container
from app.services.gemini_service import GeminiUnavailableError

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/agent", tags=["agent"])


@router.post("/run", response_model=AgentResponse)
async def run_agent(
    request: AgentRequest,
    caller: str = Depends(limit_requests("agent", "rate_limit_agent_per_minute")),
    container: ServiceContainer = Depends(get_container),
) -> AgentResponse:
    """Plans and runs a task."""

    try:
        run = await container.agent.run(request.task, request.tools or None)
    except AgentError as error:
        # A refused plan or an unknown tool is the caller's to fix, not the deployment's.
        status_code = (
            status.HTTP_400_BAD_REQUEST
            if error.code in {"TOOL_UNKNOWN", "NO_TOOLS_AVAILABLE", "PLAN_EMPTY"}
            else status.HTTP_500_INTERNAL_SERVER_ERROR
        )
        raise HTTPException(status_code=status_code, detail=str(error)) from error
    except GeminiUnavailableError as error:
        logger.warning("An agent run could not reach the model: %s", error.code)
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="The AI service is not reachable right now.",
        ) from error

    if run.status == "failed":
        # The run did not finish. A 200 here would tell the desktop client to report success for
        # a task that did not happen.
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail={
                "message": "The agent could not complete this task.",
                "error_code": run.error_code,
                "plan": [step.model_dump() for step in run.plan],
            },
        )

    return AgentResponse(
        plan=run.plan,
        result=run.result,
        status=run.status,
        error_code=run.error_code,
        model=run.model,
        tokens_used=run.tokens_used,
        latency_ms=run.latency_ms,
    )


@router.get("/tools")
async def list_tools(
    caller: str = Depends(limit_requests("agent", "rate_limit_agent_per_minute")),
    container: ServiceContainer = Depends(get_container),
) -> dict[str, list[dict[str, str]]]:
    """Lists the tools the agent may use.

    Present so the desktop client can show what the cloud agent can do, and so that a name which
    is not in this list is visibly not available rather than discovered as a 400 later.
    """

    registry = container.tool_registry

    return {
        "tools": [
            {"name": name, "description": _describe(registry, name)} for name in registry.names
        ]
    }


def _describe(registry, name: str) -> str:
    """The description for one registered tool."""

    return registry.get(name).description


__all__ = ["AgentStep", "router"]
