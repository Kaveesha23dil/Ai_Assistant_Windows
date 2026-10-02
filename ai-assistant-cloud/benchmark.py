"""Latency and throughput, measured against the budgets the desktop client assumes.

Run with ``python benchmark.py``. It uses a scripted model by default, so it runs with no
credentials and no cost, and what it measures is the overhead this service adds: middleware,
schemas, vector search and serialisation. That part is ours, a regression in it is a regression in
how the assistant feels, and it would not show up in a functional test.

Gemini's own latency is not measured here because it varies with region and load and would make
the numbers incomparable between runs. To measure the whole path, point the service at real Gemini
with ``GEMINI_API_KEY`` or ``PROJECT_ID`` and run it against a running instance.
"""

from __future__ import annotations

import asyncio
import json
import statistics
import time
from dataclasses import dataclass, field

from app.config.settings import Settings
from app.services.agent_service import AgentService
from app.services.rag_service import RAGService
from app.services.vector_database import VectorRecord
from tests.fakes import (
    FakeEmbeddingClient,
    FakeGeminiClient,
    make_embeddings,
    make_gemini,
    make_settings,
    make_vectors,
)
from tests.support import running_client

BUDGETS_MS = {
    "chat (no retrieval)": 250,
    "chat (grounded)": 400,
    "streaming, first event": 200,
    "embeddings, batch of 16": 250,
    "agent run": 400,
    "refused request": 25,
    "health": 25,
}
"""What the desktop client can hide behind a spinner.

A streamed answer is the exception: the first event is what somebody waits for, and the rest arrives
as it is written. A refusal is on the same footing as a health check: it should be instant, because
it is the path taken when something is misconfigured.
"""

ROUNDS = 25


@dataclass(slots=True)
class Measurement:
    """A series of timings and what they came out at."""

    name: str
    samples_ms: list[float] = field(default_factory=list)

    @property
    def median_ms(self) -> float:
        """The middle value.

        The median rather than the mean, because one garbage-collection pause moves a mean by a
        large fraction and the point of this number is to be comparable between runs.
        """

        return statistics.median(self.samples_ms) if self.samples_ms else 0.0

    @property
    def p95_ms(self) -> float:
        """The value 95% of requests came in under.

        This is what somebody experiences as "it is fine, it is just slow sometimes", and it is the
        figure to compare against a budget.
        """

        if not self.samples_ms:
            return 0.0

        ordered = sorted(self.samples_ms)

        return ordered[min(len(ordered) - 1, int(len(ordered) * 0.95))]

    @property
    def per_second(self) -> float:
        """Requests per second at the median."""

        return 1000.0 / self.median_ms if self.median_ms else 0.0

    def report(self) -> str:
        """One line for the console."""

        return (
            f"{self.name:<26} median {self.median_ms:6.1f} ms  p95 {self.p95_ms:6.1f} ms  "
            f"{self.per_second:7.0f}/s  n={len(self.samples_ms)}"
        )


def _measure(name: str, rounds: int, call) -> Measurement:
    """Times something repeated, keeping every sample rather than only the total."""

    measurement = Measurement(name)

    for _ in range(rounds):
        started = time.perf_counter()
        call()
        measurement.samples_ms.append((time.perf_counter() - started) * 1000)

    return measurement


def _first_streamed_event(client, message: str) -> None:
    """Opens a stream and reads the first event.

    Everything after the first event is not latency as a person experiences it: it arrives while
    they are reading.
    """

    with client.stream("POST", "/api/chat/stream", json={"message": message}) as response:
        for line in response.iter_lines():
            if line.startswith("data: "):
                return


async def _prepared(settings: Settings) -> dict[str, object]:
    """The services, with one document indexed so retrieval has something to find."""

    gemini = make_gemini(settings, FakeGeminiClient(["A grounded, considered answer."]))
    embeddings = make_embeddings(settings)
    vectors = make_vectors()

    probe = await embeddings.embed_query("probe")
    await vectors.upsert(
        [
            VectorRecord(
                id="refunds-0",
                text="Refunds are issued to the original payment method within 14 days.",
                vector=probe,
                metadata={"title": "refunds"},
            )
        ]
    )

    rag = RAGService(settings, gemini=gemini, embeddings=embeddings, vectors=vectors)

    scripted_plan = json.dumps(
        {
            "steps": [
                {
                    "tool": "knowledge_search",
                    "description": "Search the knowledge base",
                    "arguments": {"query": "refunds"},
                }
            ]
        }
    )
    agent = AgentService(
        settings,
        gemini=make_gemini(
            settings, FakeGeminiClient([scripted_plan, "Nothing further.", "A summary."])
        ),
        rag=rag,
        embeddings=embeddings,
    )

    return {
        "gemini": gemini,
        "embeddings": embeddings,
        "vectors": vectors,
        "rag": rag,
        "agent": agent,
    }


async def benchmark(rounds: int = ROUNDS) -> list[Measurement]:
    """Measures each budgeted operation against an open service, then a closed one."""

    settings = make_settings().model_copy(
        update={"rate_limit_chat_per_minute": 100_000, "log_level": "warning"}
    )
    services = await _prepared(settings)

    with running_client(settings, **services) as client:
        passages = [f"passage number {index}" for index in range(16)]

        open_measurements = [
            _measure(
                "chat (no retrieval)",
                rounds,
                lambda: client.post(
                    "/api/chat", json={"message": "What is 2 + 2?", "useKnowledge": False}
                ),
            ),
            _measure(
                "chat (grounded)",
                rounds,
                lambda: client.post(
                    "/api/chat", json={"message": "How long does a refund take?"}
                ),
            ),
            _measure(
                "streaming, first event",
                rounds,
                lambda: _first_streamed_event(client, "Explain this architecture"),
            ),
            _measure(
                "embeddings, batch of 16",
                rounds,
                lambda: client.post("/api/embeddings", json={"texts": passages}),
            ),
            _measure(
                "agent run",
                rounds,
                lambda: client.post("/api/agent/run", json={"task": "Summarise the notes"}),
            ),
            _measure("health", rounds, lambda: client.get("/health")),
        ]

    secured = settings.model_copy(update={"require_authentication": True})
    secured_services = await _prepared(secured)

    with running_client(secured, **secured_services) as client:
        refused = _measure(
            "refused request",
            rounds,
            lambda: client.post("/api/chat", json={"message": "A question"}),
        )

        # If the refusal stopped being a refusal the number above would be measuring an
        # authenticated round trip instead, which is a different thing entirely.
        assert client.post("/api/chat", json={"message": "A question"}).status_code == 401

    return [*open_measurements, refused]


def main() -> int:
    """Runs the benchmark, prints the report, and returns non-zero if a budget was missed."""

    results = asyncio.run(benchmark())
    over_budget: list[Measurement] = []

    print(f"{len(results)} measurements, {ROUNDS} rounds each\n")

    for measurement in results:
        print(measurement.report())

        budget = BUDGETS_MS.get(measurement.name)

        if budget is not None and measurement.p95_ms > budget:
            over_budget.append(measurement)

    print()

    for measurement in over_budget:
        print(
            f"OVER BUDGET  {measurement.name}: p95 {measurement.p95_ms:.1f} ms "
            f"exceeds {BUDGETS_MS[measurement.name]} ms"
        )

    if not over_budget:
        print("Every measurement is inside its budget.")

    return 1 if over_budget else 0


if __name__ == "__main__":
    raise SystemExit(main())
