"""The cloud agent: plan with Gemini, run the steps, report what happened.

Reasoning moves to the cloud and the tools are looked up by name in a registry, so a plan that
invents a tool it was not given is refused rather than executed. The steps run in order and a
failure stops the run, which is what makes the reported status mean something: a "completed"
agent run is one where every planned step actually ran.
"""

from __future__ import annotations

import abc
import json
import logging
import re
import time
from dataclasses import dataclass, field
from typing import Any

from app.config.settings import Settings, get_settings
from app.models.schemas import AgentStep
from app.services.embedding_service import EmbeddingService
from app.services.gemini_service import GeminiService, GeminiUnavailableError
from app.services.rag_service import RAGService

logger = logging.getLogger(__name__)


class AgentError(RuntimeError):
    """The agent could not complete the task, with a code the client can act on."""

    def __init__(self, message: str, code: str = "AGENT_FAILED") -> None:
        super().__init__(message)
        self.code = code


class CloudTool(abc.ABC):
    """One capability the cloud agent can use.

    Abstracted so a tool can be a call to the knowledge base, a call to Gemini, or a plain
    function, without the executor caring which.
    """

    _tokens: int = 0
    """Tokens spent by this tool instance. Read through the class attribute until the first charge
    and then from the instance, so each tool keeps its own total."""

    @property
    @abc.abstractmethod
    def name(self) -> str:
        """The name a plan refers to this tool by."""

    @property
    @abc.abstractmethod
    def description(self) -> str:
        """What the planner is told about it."""

    @property
    def requires_user_data(self) -> bool:
        """Whether this tool handles content that came from a person.

        Used by the vision tool to mark a request as one that must not be logged, so the rule
        travels with the tool rather than living only in a comment in a route.
        """

        return False

    @property
    def tokens_used(self) -> int:
        """Tokens this tool has spent since it was constructed.

        Read by the executor, which compares it before and after a step so that a run is charged
        only for its own calls. A tool that does not call a model reports zero rather than being
        absent, so the accounting is a sum over every step regardless of what kind it was.
        """

        return self._tokens

    def _charge(self, tokens: int) -> None:
        """Adds one call's tokens to this tool's running total.

        Augmented onto the instance, never onto the class. A shared counter would let one tool's
        spend appear in every other tool's total, and the executor would then charge a run for
        tools it never ran.
        """

        self._tokens += max(0, tokens)

    @abc.abstractmethod
    async def run(self, arguments: dict[str, Any]) -> str:
        """Runs the tool and returns what it produced."""


class KnowledgeSearchTool(CloudTool):
    """Searches the knowledge base."""

    def __init__(self, rag: RAGService) -> None:
        self._rag = rag

    @property
    def name(self) -> str:
        """The planner's name for this tool."""

        return "knowledge_search"

    @property
    def description(self) -> str:
        """Tells the planner when to reach for this."""

        return (
            "Search the user's knowledge base for passages relevant to a question. Use it for "
            "anything about the user's own documents."
        )

    async def run(self, arguments: dict[str, Any]) -> str:
        """Returns the matching passages as text for the plan to quote."""

        query = str(arguments.get("query", "")).strip()

        if not query:
            raise AgentError("knowledge_search needs a query.", "TOOL_ARGUMENT_MISSING")

        matches = await self._rag.retrieve(query)
        self._charge(self._rag.last_tokens_used)

        if not matches:
            return "No passages in the knowledge base matched."

        return "\n\n".join(
            f"[{index}] {match.record.metadata.get('title', 'Untitled')}\n{match.record.text}"
            for index, match in enumerate(matches, start=1)
        )


class DocumentAnalysisTool(CloudTool):
    """Summarises or analyses text handed to the agent."""

    def __init__(self, gemini: GeminiService) -> None:
        self._gemini = gemini

    @property
    def name(self) -> str:
        """The planner's name for this tool."""

        return "document_analysis"

    @property
    def description(self) -> str:
        """Tells the planner when to reach for this."""

        return (
            "Analyse, summarise, or extract structure from text the user has provided in the "
            "task or returned by another tool."
        )

    async def run(self, arguments: dict[str, Any]) -> str:
        """Returns the analysis of the supplied text."""

        text = str(arguments.get("text", "")).strip()
        instruction = str(arguments.get("instruction", "Summarise this text.")).strip()

        if not text:
            raise AgentError("document_analysis needs text to analyse.", "TOOL_ARGUMENT_MISSING")

        result = await self._gemini.generate_text(
            f"{instruction}\n\n{text}",
            system_instruction=(
                "You are analysing a document somebody asked about. Be concise and factual."
            ),
        )
        self._charge(result.tokens_used)

        return result.text


class ReportGeneratorTool(CloudTool):
    """Composes a report from material another step produced."""

    def __init__(self, gemini: GeminiService) -> None:
        self._gemini = gemini

    @property
    def name(self) -> str:
        """The planner's name for this tool."""

        return "report_generator"

    @property
    def description(self) -> str:
        """Tells the planner when to reach for this."""

        return (
            "Write a structured report from findings. Use it once there is something to report "
            "on, and pass the findings in the 'content' argument."
        )

    async def run(self, arguments: dict[str, Any]) -> str:
        """Returns the composed report."""

        content = str(arguments.get("content", "")).strip()
        title = str(arguments.get("title", "Report")).strip()

        if not content:
            raise AgentError("report_generator needs content to report on.", "TOOL_ARGUMENT_MISSING")

        result = await self._gemini.generate_text(
            content,
            system_instruction=(
                "Write a short structured report with a heading and a few sections. This will be "
                "read on a desktop screen, so keep it to something a person will finish."
            ),
            max_tokens=1500,
        )
        self._charge(result.tokens_used)

        return f"# {title}\n\n{result.text}"


class GeminiVisionTool(CloudTool):
    """Explains an image.

    Marked as handling user data so that a run using it is not logged with its steps. The image
    itself is passed in as an argument by the caller and is dropped when the call returns.
    """

    def __init__(self, gemini: GeminiService) -> None:
        self._gemini = gemini

    @property
    def name(self) -> str:
        """The planner's name for this tool."""

        return "gemini_vision"

    @property
    def description(self) -> str:
        """Tells the planner when to reach for this."""

        return (
            "Look at an image the user captured and explain what is in it, including error "
            "dialogues and charts. Use it when the task refers to something on screen."
        )

    @property
    def requires_user_data(self) -> bool:
        """This tool handles a screenshot."""

        return True

    async def run(self, arguments: dict[str, Any]) -> str:
        """Returns what the image shows."""

        import base64

        encoded = str(arguments.get("image_base64", "")).strip()
        question = str(arguments.get("question", "Describe this image.")).strip()

        if not encoded:
            raise AgentError("gemini_vision needs an image.", "TOOL_ARGUMENT_MISSING")

        try:
            image_bytes = base64.b64decode(encoded, validate=True)
        except Exception as error:  # noqa: BLE001
            raise AgentError(
                "The image could not be decoded.", "IMAGE_NOT_BASE64"
            ) from error

        result = await self._gemini.analyze_image(
            image_bytes=image_bytes,
            mime_type=str(arguments.get("mime_type", "image/png")),
            question=question,
            instruction="Explain what this image shows and what it means.",
        )
        self._charge(result.tokens_used)

        return result.text


class CloudToolRegistry:
    """The tools the cloud agent may use, by name.

    A name that is not registered is refused. That refusal is the whole security model of the
    agent endpoint: a model that hallucinates a tool name gets a clear error instead of an
    attempt to run something.
    """

    def __init__(self, tools: list[CloudTool] | None = None) -> None:
        self._tools: dict[str, CloudTool] = {}

        for tool in tools or []:
            self.register(tool)

    def register(self, tool: CloudTool) -> None:
        """Adds a tool, replacing one of the same name."""

        if not tool.name or not tool.name.replace("_", "").replace("-", "").isalnum():
            raise ValueError(f"{tool.name!r} is not a usable tool name.")

        self._tools[tool.name.lower()] = tool

    def get(self, name: str) -> CloudTool:
        """Looks a tool up, or refuses the name."""

        key = name.strip().lower()

        if key not in self._tools:
            raise AgentError(
                f"There is no tool called {key!r}. Available: {', '.join(self.names)}.",
                "TOOL_UNKNOWN",
            )

        return self._tools[key]

    @property
    def names(self) -> list[str]:
        """Every registered name, sorted, for the planner's prompt and for errors."""

        return sorted(self._tools)

    def resolve(self, requested: list[str] | None) -> list[CloudTool]:
        """Resolves the requested names, or the full set when none were requested.

        ``None`` and ``[]`` mean different things and are treated differently. ``None`` is a caller
        that did not express a preference and gets everything; an empty list is a caller that asked
        for no tools, and handing it the full set would run capabilities it had excluded on purpose.
        The run is then refused by the caller as having nothing to do, which is a clearer outcome
        than silently doing the work anyway.

        A requested name that does not exist is an error rather than a silent omission: a plan
        that quietly lost a step would report success for work it did not do.
        """

        if requested is None:
            return [self._tools[name] for name in self.names]

        return [self.get(name) for name in requested]

    def describe(self, tools: list[CloudTool]) -> str:
        """The tool list as it appears in the planner's instruction."""

        return "\n".join(f"- {tool.name}: {tool.description}" for tool in tools)

    def involves_user_data(self, tools: list[CloudTool]) -> bool:
        """Whether any of these tools handles something a person can see."""

        return any(tool.requires_user_data for tool in tools)


@dataclass(slots=True)
class AgentRun:
    """The outcome of a run."""

    plan: list[AgentStep] = field(default_factory=list)
    result: str = ""
    status: str = "completed"
    error_code: str | None = None
    tokens_used: int = 0
    latency_ms: int = 0
    model: str | None = None


class AgentService:
    """Plans a task, runs the steps, and reports honestly on what happened."""

    def __init__(
        self,
        settings: Settings | None = None,
        gemini: GeminiService | None = None,
        rag: RAGService | None = None,
        embeddings: EmbeddingService | None = None,
        registry: CloudToolRegistry | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._gemini = gemini or GeminiService(self._settings)
        self._rag = rag or RAGService(
            self._settings, gemini=self._gemini, embeddings=embeddings
        )
        self._registry = registry or CloudToolRegistry(
            [
                KnowledgeSearchTool(self._rag),
                DocumentAnalysisTool(self._gemini),
                ReportGeneratorTool(self._gemini),
                GeminiVisionTool(self._gemini),
            ]
        )

    @property
    def registry(self) -> CloudToolRegistry:
        """The registry in use."""

        return self._registry

    @property
    def rag(self) -> RAGService:
        """The retrieval service in use.

        Public so a caller building its own tool, or a test, can reach the same retrieval the
        agent's own knowledge tool uses.
        """

        return self._rag

    async def run(self, task: str, requested_tools: list[str] | None = None) -> AgentRun:
        """Runs a task end to end."""

        started = time.perf_counter()
        tools = self._registry.resolve(requested_tools)

        if not tools:
            raise AgentError(
                "No tools are available to the agent.", "NO_TOOLS_AVAILABLE"
            )

        plan, plan_tokens = await self._plan(task, tools)

        if not plan:
            raise AgentError(
                "The agent could not produce a plan for this task.", "PLAN_EMPTY"
            )

        result_text, status, error_code, step_tokens = await self._execute(plan, task)

        return AgentRun(
            plan=plan,
            result=result_text,
            status=status,
            error_code=error_code,
            # Every token spent on this run, because a plan that used a model call and steps that
            # used several more is one cost, and reporting only the last call would understate it.
            tokens_used=plan_tokens + step_tokens,
            latency_ms=max(0, int((time.perf_counter() - started) * 1000)),
            model=self._gemini.model_name,
        )

    async def _plan(self, task: str, tools: list[CloudTool]) -> tuple[list[AgentStep], int]:
        """Asks Gemini for a plan and parses it.

        The plan is JSON because a plan in prose cannot be executed or reported on. A response
        that will not parse is a refusal rather than a guess at what was meant, because running a
        guessed plan can mean running a step nobody chose.
        """

        instruction = (
            "Plan how to accomplish the task using only the tools listed. Reply with JSON only, "
            "in the form {\"steps\": [{\"tool\": \"tool_name\", \"description\": \"what this step "
            "does\", \"arguments\": {}}]}. Use between one and five steps, in the order they "
            "should run. Use a tool only for what its description says it is for."
        )

        result = await self._gemini.generate_text(
            f"{task}\n\nAvailable tools:\n{self._registry.describe(tools)}",
            system_instruction=instruction,
            temperature=0.1,
        )

        return self._parse_plan(result.text, tools), result.tokens_used

    def _parse_plan(self, text: str, tools: list[CloudTool]) -> list[AgentStep]:
        """Reads the plan out of a model response.

        Tolerant of a fenced code block and of surrounding prose, because models wrap JSON
        without being asked to and refusing the whole run over a stray fence would be a poor
        trade. Strict about the tool names: a step naming a tool that was not offered is
        dropped, and if that leaves nothing then the run is refused.
        """

        payload = self._extract_json(text)

        if payload is None:
            return []

        raw_steps = payload.get("steps") if isinstance(payload, dict) else None

        if not isinstance(raw_steps, list):
            return []

        known = {tool.name for tool in tools}
        steps: list[AgentStep] = []
        order = 0

        for entry in raw_steps:
            if not isinstance(entry, dict):
                continue

            tool_name = str(entry.get("tool", "")).strip().lower()

            # A tool that was not offered is not run, and saying so is more useful than silently
            # dropping it: the caller can see the plan is short of what the model wanted.
            if tool_name not in known:
                logger.warning(
                    "A planned step named the unknown tool %r and was not included.", tool_name
                )
                continue

            order += 1
            arguments = entry.get("arguments")

            steps.append(
                AgentStep(
                    order=order,
                    tool=tool_name,
                    description=str(entry.get("description", "")).strip() or tool_name,
                    arguments=arguments if isinstance(arguments, dict) else {},
                )
            )

        return steps[:5]

    async def _execute(
        self, plan: list[AgentStep], task: str
    ) -> tuple[str, str, str | None, int]:
        """Runs the steps in order.

        A failing step stops the run. Carrying on would mean reporting a result built from a
        foundation that is missing, and the status "partial" exists to say exactly that.
        """

        outputs: list[str] = []
        error_code: str | None = None
        tokens = 0

        for step in plan:
            try:
                tool = self._registry.get(step.tool)
                charged_before = tool.tokens_used
                output = await tool.run(step.arguments)
            except (AgentError, GeminiUnavailableError) as error:
                step.status = "failed"
                error_code = getattr(error, "code", "AGENT_FAILED")
                logger.warning(
                    "Agent step %d (%s) failed: %s", step.order, step.tool, error_code
                )
                return ("\n\n".join(outputs), "partial" if outputs else "failed", error_code, tokens)

            step.status = "completed"
            step.output = output[:2000]
            outputs.append(f"[{step.order}] {step.description}\n{output}")

            # The difference, not the running total. A tool's counter is cumulative across every
            # run in the process, so adding it would charge this run for every earlier run and the
            # reported cost would climb without any corresponding work.
            tokens += max(0, tool.tokens_used - charged_before)

        summary = await self._summarise(task, outputs)
        tokens += self._gemini.last_tokens_used

        return summary, "completed", None, tokens

    async def _summarise(self, task: str, outputs: list[str]) -> str:
        """Composes the final answer from what the steps produced."""

        if not outputs:
            return ""

        result = await self._gemini.generate_text(
            f"Task: {task}\n\nWhat the steps found:\n\n" + "\n\n".join(outputs),
            system_instruction=(
                "Write the final answer to the user's task using what the steps found. Be "
                "concise and concrete. Do not describe the steps themselves."
            ),
            max_tokens=1500,
        )

        return result.text

    @staticmethod
    def _extract_json(text: str) -> dict[str, Any] | None:
        """Finds a JSON object in a model response."""

        fenced = re.search(r"```(?:json)?\s*(.+?)```", text, re.DOTALL)

        if fenced:
            try:
                parsed = json.loads(fenced.group(1))
            except json.JSONDecodeError:
                parsed = None

            if isinstance(parsed, dict):
                return parsed

        start = text.find("{")
        end = text.rfind("}")

        if start < 0 or end <= start:
            return None

        try:
            parsed = json.loads(text[start : end + 1])
        except json.JSONDecodeError:
            return None

        return parsed if isinstance(parsed, dict) else None
