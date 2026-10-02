"""The vector store contract, and the two implementations behind it.

The application never names a concrete store. The knowledge and agent services ask an
``IVectorDatabase`` for nearest neighbours and know nothing about where the vectors live, so
moving from the in-process store to Vertex AI Vector Search is a configuration change rather
than a rewrite, and neither store's behaviour leaks into the answer a person gets.
"""

from __future__ import annotations

import abc
import logging
import math
import threading
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any

logger = logging.getLogger(__name__)

_DELETE_BATCH = 100
"""How many ids to ask Vertex AI Vector Search to delete in one call."""


@dataclass(slots=True)
class VectorRecord:
    """One stored passage and its vector."""

    id: str
    text: str
    vector: list[float]
    metadata: dict[str, Any] = field(default_factory=dict)
    created_at: datetime | None = None

    def __post_init__(self) -> None:
        if not self.id:
            raise ValueError("A vector record needs an id.")
        if not self.text:
            raise ValueError("A vector record needs the text it was embedded from.")
        if not self.vector:
            raise ValueError("A vector record needs a vector.")

        if self.created_at is None:
            self.created_at = datetime.now(timezone.utc)


@dataclass(slots=True)
class SearchMatch:
    """One retrieved passage and how well it matched."""

    record: VectorRecord
    score: float


class IVectorDatabase(abc.ABC):
    """What the application needs from a vector store.

    Five operations. Deletion is here rather than left out because a knowledge base a person
    cannot remove a document from is a privacy problem before it is a feature problem: they
    deleted the file, and the assistant still quotes it. Ids are therefore stable and derivable
    from the document, so a document can be removed without first listing what it wrote.
    """

    @property
    @abc.abstractmethod
    def name(self) -> str:
        """The implementation name, reported by the health check."""

    @abc.abstractmethod
    async def upsert(self, records: list[VectorRecord]) -> int:
        """Adds or replaces records. Returns how many were written."""

    @abc.abstractmethod
    async def delete(self, record_ids: list[str]) -> int:
        """Removes records by id. Returns how many were actually removed.

        Ids that are not present count as already removed rather than as an error, so removing a
        document twice succeeds and removing one that never existed is not a failure the caller
        has to interpret.
        """

    @abc.abstractmethod
    async def search(
        self,
        vector: list[float],
        top_k: int = 8,
        minimum_score: float = 0.0,
    ) -> list[SearchMatch]:
        """Returns the nearest records, best first."""

    @abc.abstractmethod
    async def count(self) -> int:
        """How many records are stored."""

    @abc.abstractmethod
    async def clear(self) -> int:
        """Removes every record. Returns how many were removed."""


class InMemoryVectorDatabase(IVectorDatabase):
    """An in-process store, used by the tests and by a first local run.

    Holds vectors in a dictionary behind a lock and scans them. It is a linear scan and it is
    written that way on purpose: it is fast enough for the tens of thousands of chunks a single
    user's knowledge base holds, and it needs no container, so the whole RAG path can be
    exercised and tested without cloud credentials or a database.
    """

    def __init__(self, expected_dimensions: int | None = None) -> None:
        self._records: dict[str, VectorRecord] = {}
        self._lock = threading.Lock()
        self._expected_dimensions = expected_dimensions

    @property
    def name(self) -> str:
        """Names itself for the health check."""

        return "in-memory"

    async def upsert(self, records: list[VectorRecord]) -> int:
        """Adds or replaces records by id."""

        with self._lock:
            for record in records:
                self._check_dimensions(record.vector, record.id)
                self._records[record.id] = record

            return len(records)

    async def delete(self, record_ids: list[str]) -> int:
        """Removes records by id and reports how many were there to remove."""

        with self._lock:
            return sum(1 for record_id in record_ids if self._records.pop(record_id, None))

    async def search(
        self,
        vector: list[float],
        top_k: int = 8,
        minimum_score: float = 0.0,
    ) -> list[SearchMatch]:
        """Scans every record and returns the closest ones.

        Every stored vector is compared against the query with a cosine similarity. Both sides
        are expected to be unit length already, so the division is only a guard against a vector
        that arrived from somewhere else.
        """

        limit = max(1, top_k)

        with self._lock:
            candidates = list(self._records.values())

        matches = [
            SearchMatch(record=record, score=self._cosine(vector, record.vector))
            for record in candidates
        ]

        matches = [match for match in matches if match.score >= minimum_score]

        # Ties are broken by id so the order is stable between two identical queries. Without
        # that, a retriever can return the same passages in a different order each time and a
        # person sees a different answer to the same question.
        matches.sort(key=lambda match: (-match.score, match.record.id))

        return matches[:limit]

    async def count(self) -> int:
        """How many records are held."""

        with self._lock:
            return len(self._records)

    async def clear(self) -> int:
        """Removes everything and returns how many were removed."""

        with self._lock:
            removed = len(self._records)
            self._records.clear()
            return removed

    def _check_dimensions(self, vector: list[float], record_id: str) -> None:
        """Refuses a vector of the wrong width.

        Vectors of different widths cannot be compared, and finding that out at query time
        produces a wrong answer rather than an error. A mixed-width index would have to be
        discarded and rebuilt anyway, so it is refused at write time.
        """

        if self._expected_dimensions is None:
            self._expected_dimensions = len(vector)
            return

        if len(vector) != self._expected_dimensions:
            raise ValueError(
                f"Record {record_id} has {len(vector)} dimensions but this store holds "
                f"{self._expected_dimensions}. A mixed-width index cannot be searched."
            )

    @staticmethod
    def _cosine(left: list[float], right: list[float]) -> float:
        """Cosine similarity, clamped to the range a score is expected to be in."""

        if len(left) != len(right):
            logger.warning(
                "Comparing vectors of different widths (%d and %d); scoring them as unrelated.",
                len(left),
                len(right),
            )
            return 0.0

        dot = sum(a * b for a, b in zip(left, right, strict=True))
        left_length = math.sqrt(sum(component * component for component in left))
        right_length = math.sqrt(sum(component * component for component in right))

        if left_length == 0.0 or right_length == 0.0:
            return 0.0

        score = dot / (left_length * right_length)

        # Floating point can push a vector against itself a hair past 1.0, and a score above 1
        # is rejected by the response schema.
        return max(-1.0, min(1.0, score))


class VertexVectorDatabase(IVectorDatabase):
    """Vertex AI Vector Search, used in a deployed environment.

    Talks to the REST surface of the index rather than through a client library, which keeps the
    image small and every request legible in a log. The operations are the documented v1 ones on
    the index itself (``upsertVectors``, ``findNearestNeighbors``, ``mutateVectors``,
    ``countVectors``) reached through the regional endpoint, so the project and location are in the
    path rather than guessed into a hostname. The access token comes from Application Default
    Credentials, so this class holds no secret of its own.
    """

    def __init__(
        self,
        project_id: str,
        region: str,
        index_id: str = "",
        endpoint_override: str = "",
        timeout_seconds: float = 30.0,
    ) -> None:
        self._project_id = project_id
        self._region = region
        self._index_id = index_id
        self._endpoint_override = endpoint_override
        self._timeout = timeout_seconds

    @property
    def name(self) -> str:
        """Names itself for the health check."""

        return "vertex-ai-vector-search"

    @property
    def endpoint(self) -> str:
        """The regional API endpoint.

        Overridable so a deployment behind a private endpoint, or a test double, can point
        somewhere else without changing the request paths.
        """

        return self._endpoint_override or f"https://{self._region}-aiplatform.googleapis.com"

    @property
    def index_path(self) -> str:
        """The index resource path every operation hangs off."""

        return (
            f"/v1/projects/{self._project_id}/locations/{self._region}"
            f"/indexes/{self._index_id}"
        )

    def _url(self, operation: str) -> str:
        """The full URL for one index operation."""

        if not self._index_id:
            raise RuntimeError(
                "VECTOR_SEARCH_INDEX_ID is not configured, so there is no index to talk to."
            )

        return f"{self.endpoint}{self.index_path}:{operation}"

    async def upsert(self, records: list[VectorRecord]) -> int:
        """Writes records to the index."""

        if not records:
            return 0

        await self._post(
            "upsertVectors",
            {
                "requests": [
                    {
                        "datapoint": {
                            "datapointId": record.id,
                            "datapointValues": record.vector,
                            "featureLabels": [
                                {"key": "title", "value": str(record.metadata.get("title", ""))},
                                {"key": "text", "value": record.text},
                            ],
                        }
                    }
                    for record in records
                ]
            },
        )

        return len(records)

    async def delete(self, record_ids: list[str]) -> int:
        """Removes records by id.

        Deletion is by id in batches, because the API accepts a list per call and a whole document
        can be tens of chunks. Ids that are not present are not an error: the index does not report
        which of the requested ids existed, and the caller only cares that the document is gone.
        """

        if not record_ids:
            return 0

        await self._post(
            "mutateVectors",
            {
                "requests": [
                    {
                        "operation": "delete",
                        "datapoint": {"datapointId": record_id},
                    }
                    for record_id in record_ids
                ]
            },
        )

        return len(record_ids)

    async def search(
        self,
        vector: list[float],
        top_k: int = 8,
        minimum_score: float = 0.0,
    ) -> list[SearchMatch]:
        """Queries the index for the nearest records.

        The API returns a distance and this service is written in terms of similarity, so the
        distance is inverted. Getting that backwards would rank the least relevant passage first,
        which is the sort of bug that survives a demo because a demo store is small.
        """

        response = await self._post(
            "findNearestNeighbors",
            {
                "deployedIndexId": self._index_id,
                "queries": [
                    {
                        "datapoint": {"datapointValues": vector},
                        "neighborCount": max(1, top_k),
                        "returnFullDatapoint": True,
                    }
                ],
            },
        )

        matches: list[SearchMatch] = []

        for query in response.get("nearestNeighbors", []):
            for neighbor in query.get("matches", []):
                distance = float(neighbor.get("distance", 1.0))
                similarity = max(-1.0, min(1.0, 1.0 - distance))

                if similarity < minimum_score:
                    continue

                datapoint = neighbor.get("datapoint", {}).get("datapoint", {})
                labels = {
                    label.get("key", ""): label.get("value", "")
                    for label in datapoint.get("featureLabels", [])
                }

                matches.append(
                    SearchMatch(
                        record=VectorRecord(
                            id=neighbor.get("id", ""),
                            text=labels.get("text", ""),
                            vector=datapoint.get("datapointValues", []),
                            metadata={"title": labels.get("title", "Untitled")},
                        ),
                        score=similarity,
                    )
                )

        matches.sort(key=lambda match: (-match.score, match.record.id))

        return matches[: max(1, top_k)]

    async def count(self) -> int:
        """The number of records in the index, read from its statistics."""

        response = await self._get(f"/indexes/{self._index_id}/stats")

        return int(response.get("vectorsCount", 0))

    async def clear(self) -> int:
        """Empties the index by deleting in batches.

        Vertex AI Vector Search has no delete-all, so this walks the index looking for ids to
        remove. That is genuinely expensive on a large index and it is why a rebuild is normally
        done by creating a new index rather than by emptying this one; the operation exists so a
        test or a small deployment has one way to start over rather than two.

        Returns how many records were there beforehand, which is the only count the API will give.
        """

        previous = await self.count()
        removed = 0

        while True:
            neighbours = await self._post(
                "findNearestNeighbors",
                {
                    "deployedIndexId": self._index_id,
                    "queries": [
                        {
                            "datapoint": {"datapointValues": [0.0]},
                            "neighborCount": _DELETE_BATCH,
                            "returnFullDatapoint": False,
                        }
                    ],
                },
            )

            ids = [
                neighbor.get("id", "")
                for query in neighbours.get("nearestNeighbors", [])
                for neighbor in query.get("matches", [])
                if neighbor.get("id")
            ]

            if not ids:
                break

            await self.delete(ids)
            removed += len(ids)

            if len(ids) < _DELETE_BATCH:
                break

        return previous or removed

    async def _post(self, operation: str, body: dict[str, Any]) -> dict[str, Any]:
        """Posts to one index operation."""

        import httpx

        token = await self._access_token()

        async with httpx.AsyncClient(timeout=self._timeout) as client:
            response = await client.post(
                self._url(operation),
                json=body,
                headers={"Authorization": f"Bearer {token}"},
            )
            response.raise_for_status()

            # The operations return an empty object with a 200, and asking for JSON on an empty
            # body would raise for a request that actually succeeded.
            return response.json() if response.content else {}

    async def _get(self, path: str) -> dict[str, Any]:
        """Gets a resource from the Vertex AI REST API."""

        import httpx

        token = await self._access_token()

        async with httpx.AsyncClient(timeout=self._timeout) as client:
            response = await client.get(
                f"{self.endpoint}/v1/projects/{self._project_id}"
                f"/locations/{self._region}{path}",
                headers={"Authorization": f"Bearer {token}"},
            )
            response.raise_for_status()

            return response.json() if response.content else {}

    @staticmethod
    async def _access_token() -> str:
        """Reads an access token from Application Default Credentials.

        The credential itself is never read here. On Cloud Run the metadata server already has a
        token for the service account the revision is running as, so the code that would hold a
        key is not written.
        """

        import google.auth  # Imported lazily, as above.
        import google.auth.transport.requests  # noqa: F401 - registers the transport

        try:
            credentials, _ = google.auth.default(
                scopes=["https://www.googleapis.com/auth/cloud-platform"]
            )
        except Exception as error:  # noqa: BLE001
            raise RuntimeError(
                "No Application Default Credentials are available, so the vector store cannot "
                "be reached. The service needs a service account attached to the revision."
            ) from error

        credentials.refresh(google.auth.transport.requests.Request())

        return credentials.token
