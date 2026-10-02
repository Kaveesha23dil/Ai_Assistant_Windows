"""The cloud agent: planning, execution, and the boundary around tool calls.

The important property here is negative. The agent runs a plan that a model wrote, so the rule that
matters is that a plan cannot reach anything the registry did not offer, and that a tool which
fails stops the run rather than producing a confident answer from a missing foundation.
"""

from __future__ import annotations

import json

import pytest

from app.services.agent_service import (
    AgentError,
    AgentService,
    CloudTool,
    CloudToolRegistry,
    DocumentAnalysisTool,
    GeminiVisionTool,
    KnowledgeSearchTool,
    ReportGeneratorTool,
)
from tests.fakes import FakeGeminiClient, make_embeddings, make_gemini, make_vectors


def plan_for(*tools: tuple[str, dict]) -> str:
    """A model response that plans the given steps."""

    return json.dumps(
        {
            "steps": [
                {"tool": name, "description": name, "arguments": arguments}
                for name, arguments in tools
            ]
        }
    )


def agent_for(settings, replies: list[str], **kwargs) -> AgentService:
    """An agent whose model is scripted."""

    return AgentService(
        settings,
        gemini=make_gemini(settings, FakeGeminiClient(replies, **kwargs)),
        embeddings=make_embeddings(settings),
    )


@pytest.fixture
def agent(settings) -> AgentService:
    """An agent that plans one document analysis and summarises it."""

    return AgentService(
        settings,
        gemini=make_gemini(
            settings,
            FakeGeminiClient(
                [plan_for(("document_analysis", {"text": "some notes"})), "A summary."]
            ),
        ),
        embeddings=make_embeddings(settings),
    )


# ---------------------------------------------------------------- the run


@pytest.mark.asyncio
async def test_a_run_produces_a_plan_a_result_and_a_status(agent: AgentService) -> None:
    """The shape a client renders."""

    run = await agent.run("Summarise my notes")

    assert run.status == "completed"
    assert len(run.plan) == 1
    assert run.plan[0].tool == "document_analysis"
    assert run.plan[0].status == "completed"
    assert run.result == "A summary."


@pytest.mark.asyncio
async def test_a_run_reports_what_it_spent(settings) -> None:
    """A run reports its tokens, so a caller can see what a question cost."""

    run = await agent_for(
        settings,
        [plan_for(("document_analysis", {"text": "x"})), "Done."],
    ).run("A question")

    assert run.tokens_used > 0
    assert run.latency_ms >= 0


@pytest.mark.asyncio
async def test_a_repeated_run_does_not_inflate_the_token_count(settings) -> None:
    """The second identical run costs about what the first did.

    The tools live in one registry for the life of the process and their token counters are
    cumulative. Charging a run a tool's whole lifetime total would make the same question cost more
    every time it was asked, which is both wrong and visible.
    """

    service = agent_for(
        settings,
        [plan_for(("document_analysis", {"text": "x"})) for _ in range(4)],
    )

    first = await service.run("A question")
    second = await service.run("A question")

    assert first.tokens_used == second.tokens_used


@pytest.mark.asyncio
async def test_a_plan_the_model_cannot_produce_is_refused(settings) -> None:
    """A response with no plan is an error naming the reason.

    Silence would be reported as a completed run with an empty result, which reads to a person as
    the assistant deciding there was nothing to say.
    """

    service = agent_for(settings, ["I am not going to answer in JSON."])

    with pytest.raises(AgentError) as refused:
        await service.run("A question")

    assert refused.value.code == "PLAN_EMPTY"


@pytest.mark.asyncio
async def test_a_step_naming_an_unknown_tool_is_not_run(settings) -> None:
    """A hallucinated tool name cannot become an executed step.

    This is the security property of the whole endpoint: the plan is model output, so the set of
    things that can happen is decided by the registry and not by the model. The step is dropped, and
    because nothing usable is left the run is refused rather than reported as an empty success.
    """

    service = agent_for(settings, [plan_for(("delete_everything", {})), "Done."])

    with pytest.raises(AgentError) as refused:
        await service.run("Delete everything")

    assert refused.value.code == "PLAN_EMPTY"
    assert set(service.registry.names) == {
        "knowledge_search",
        "document_analysis",
        "report_generator",
        "gemini_vision",
    }


def test_a_plan_keeps_the_known_steps_and_drops_the_rest(settings) -> None:
    """A plan naming one real tool and one invented one runs only the real one.

    Dropping rather than failing outright keeps a run useful when a model adds a step that cannot
    be honoured, and the refusal above covers the case where nothing usable is left.
    """

    service = agent_for(settings, ["unused"])
    offered = service.registry.resolve(None)

    plan = service._parse_plan(
        plan_for(
            ("document_analysis", {"text": "x"}),
            ("delete_everything", {}),
        ),
        offered,
    )

    assert [step.tool for step in plan] == ["document_analysis"]


# ---------------------------------------------------------------- failures


@pytest.mark.asyncio
async def test_a_failing_step_stops_the_run(settings) -> None:
    """A run whose only step failed is reported as failed, not as completed with no answer."""

    service = agent_for(settings, [plan_for(("knowledge_search", {}))])

    run = await service.run("Find something")

    assert run.status == "failed"
    assert run.error_code == "TOOL_ARGUMENT_MISSING"


@pytest.mark.asyncio
async def test_a_failure_after_a_success_is_reported_as_partial(settings) -> None:
    """Work that was done is kept and labelled, rather than discarded or overclaimed.

    "Partial" is the honest word for it: the first step found something and the second did not
    finish, so the run did not complete and is not pretending otherwise.
    """

    service = agent_for(
        settings,
        [
            plan_for(
                ("document_analysis", {"text": "x"}),
                ("knowledge_search", {}),
            ),
            "A summary of the first step.",
        ],
    )

    run = await service.run("Do both")

    assert run.status == "partial"
    assert run.error_code == "TOOL_ARGUMENT_MISSING"
    assert run.plan[0].status == "completed"
    assert run.plan[1].status == "failed"


@pytest.mark.asyncio
async def test_an_unreachable_model_is_reported_as_unavailable(settings) -> None:
    """A model that cannot be reached surfaces as unavailable, not as a bad plan."""

    service = AgentService(
        settings,
        gemini=make_gemini(settings, fail_with=TimeoutError("slow")),
        embeddings=make_embeddings(settings),
    )

    from app.services.gemini_service import GeminiUnavailableError

    with pytest.raises(GeminiUnavailableError):
        await service.run("A question")


# ---------------------------------------------------------------- the registry


def test_the_registry_offers_exactly_the_four_cloud_tools(agent: AgentService) -> None:
    """The advertised set is the real set, so the list a client reads is not a promise."""

    assert set(agent.registry.names) == {
        "knowledge_search",
        "document_analysis",
        "report_generator",
        "gemini_vision",
    }


def test_a_caller_can_ask_for_a_subset(agent: AgentService) -> None:
    """Restricting the tools restricts what the model was told about."""

    assert {tool.name for tool in agent.registry.resolve(["knowledge_search"])} == {
        "knowledge_search"
    }


def test_no_list_means_everything_and_an_empty_list_means_nothing(
    agent: AgentService,
) -> None:
    """``None`` and ``[]`` are different requests.

    A client that does not know what the agent has gets all of it. A client that asked for no
    tools gets none, because running the capabilities it excluded would be doing work it declined.
    """

    assert len(agent.registry.resolve(None)) == 4
    assert agent.registry.resolve([]) == []


@pytest.mark.asyncio
async def test_a_task_with_no_tools_available_is_refused(agent: AgentService) -> None:
    """Asking for no tools is reported rather than quietly doing everything anyway."""

    with pytest.raises(AgentError) as refused:
        await agent.run("A question", requested_tools=[])

    assert refused.value.code == "NO_TOOLS_AVAILABLE"


def test_an_unknown_tool_name_is_refused_by_the_registry(agent: AgentService) -> None:
    """Naming a tool that does not exist is refused before any model call."""

    with pytest.raises(AgentError):
        agent.registry.resolve(["delete_everything"])


@pytest.mark.parametrize(
    "name", ["../../etc/passwd", "run; rm -rf /", "two words", "", "a b", "tool\nname"]
)
def test_a_tool_that_could_not_be_named_cannot_be_registered(settings, name: str) -> None:
    """A name that is not a plain identifier is refused at registration.

    The registry is the boundary between a model's plan and what actually runs. A name carrying
    punctuation, a space or a path is a name somebody would rather not see in a log line or in the
    planner's prompt, so it cannot be introduced at all.
    """

    tool = DocumentAnalysisTool(make_gemini(settings, ["x"]))

    class BadlyNamed(DocumentAnalysisTool):
        @property
        def name(self) -> str:
            return name

    tool.__class__ = BadlyNamed

    with pytest.raises(ValueError, match="usable tool name"):
        CloudToolRegistry([tool])


# ---------------------------------------------------------------- the tools


@pytest.mark.asyncio
async def test_the_knowledge_tool_reports_an_empty_base_rather_than_failing(
    settings,
) -> None:
    """An empty knowledge base is an answer, not an error.

    The tool refusing would fail a run for a task that was perfectly well posed; there is simply
    nothing indexed yet.
    """

    rag = AgentService(
        settings,
        gemini=make_gemini(settings, ["x"]),
        embeddings=make_embeddings(settings),
    ).rag

    assert "No passages" in await KnowledgeSearchTool(rag).run({"query": "anything"})


@pytest.mark.asyncio
async def test_the_document_tool_refuses_an_empty_document(settings) -> None:
    """An empty document is refused with a code rather than sent to the model."""

    tool = DocumentAnalysisTool(make_gemini(settings, ["x"]))

    from app.services.agent_service import AgentError

    with pytest.raises(AgentError) as refused:
        await tool.run({"text": "   "})

    assert refused.value.code == "TOOL_ARGUMENT_MISSING"


@pytest.mark.asyncio
async def test_the_report_tool_includes_the_title_it_was_given(settings) -> None:
    """The report is the document the caller asked for, with its heading."""

    tool = ReportGeneratorTool(make_gemini(settings, ["Body text."]))

    result = await tool.run({"content": "findings", "title": "Weekly status"})

    assert result.startswith("# Weekly status")
    assert "Body text." in result


@pytest.mark.asyncio
async def test_the_vision_tool_is_marked_as_handling_user_data(settings) -> None:
    """The privacy rule travels with the tool rather than living in a comment in a route.

    A run using this tool must not be logged with its steps, and the flag is what lets the route
    know that without knowing which tool is which.
    """

    assert GeminiVisionTool(make_gemini(settings, ["x"])).requires_user_data is True


@pytest.mark.asyncio
async def test_the_other_tools_are_not_marked_as_handling_user_data(settings) -> None:
    """Only the tool that can see a screenshot carries the flag."""

    rag = AgentService(
        settings,
        gemini=make_gemini(settings, ["x"]),
        embeddings=make_embeddings(settings),
    ).rag

    assert KnowledgeSearchTool(rag).requires_user_data is False
    assert DocumentAnalysisTool(make_gemini(settings, ["x"])).requires_user_data is False
    assert ReportGeneratorTool(make_gemini(settings, ["x"])).requires_user_data is False


@pytest.mark.asyncio
async def test_each_tool_keeps_its_own_token_count(settings) -> None:
    """One tool's spending does not appear in another's.

    With a shared counter, a run that used one tool would be charged for every tool that had ever
    been used in the process, and the reported cost would bear no relation to the work done.
    """

    first = DocumentAnalysisTool(make_gemini(settings, ["a"]))
    second = DocumentAnalysisTool(make_gemini(settings, ["b"]))

    await first.run({"text": "some text"})

    assert first.tokens_used > 0
    assert second.tokens_used == 0


def test_a_tool_cannot_be_instantiated_without_its_behaviour() -> None:
    """The base class is abstract, so a tool cannot be registered without a name and a run."""

    with pytest.raises(TypeError):
        CloudTool()  # type: ignore[abstract]


def test_a_registry_can_be_built_by_hand(settings) -> None:
    """The registry does not depend on how the tools were made."""

    tool = ReportGeneratorTool(make_gemini(settings, ["x"]))
    registry = CloudToolRegistry([tool])

    assert registry.get("report_generator") is tool
