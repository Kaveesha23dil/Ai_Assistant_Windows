"""Configuration: what a deployment is allowed to do, and what it is told about.

Most of these assertions are about refusals rather than values. A setting that is accepted and then
does nothing is worse than one that is refused, because the operator believes they configured
something and finds out during a demonstration.
"""

from __future__ import annotations

import pytest

from app.config.settings import Settings, get_settings
from app.services.container import build_vector_database
from tests.fakes import make_settings


# ---------------------------------------------------------------- credentials


def test_no_credentials_means_no_model_calls() -> None:
    """A configuration with neither a key nor a project is refused.

    ``has_gemini_credentials`` is what the health check reports and what the service should refuse
    to serve traffic on, so it is asserted rather than left implied.
    """

    assert make_settings(gemini_api_key="", project_id="").has_gemini_credentials is False


def test_a_key_is_enough() -> None:
    """A local run with a key can reach the model."""

    assert make_settings(gemini_api_key="sk-test").has_gemini_credentials is True


def test_a_project_is_enough() -> None:
    """A deployed run needs no key at all: the service account is the credential."""

    assert make_settings(gemini_api_key="", project_id="a-project").has_gemini_credentials is True


def test_a_project_means_vertex_and_not_a_key() -> None:
    """Which path is taken is decided by what is configured, and said plainly."""

    assert make_settings(project_id="a-project").uses_vertex_ai is True
    assert make_settings(gemini_api_key="sk-test").uses_vertex_ai is False


# ---------------------------------------------------------------- production refusals


def test_production_without_a_signing_secret_is_a_problem() -> None:
    """Not fatal at import, but it is a problem the health check reports.

    Starting anyway is deliberate: a container that refuses to boot cannot be inspected, and the
    health check is what a deployment gate reads.
    """

    problems = make_settings(environment="production", jwt_secret="").validate_for_startup()

    assert any("JWT_SECRET" in problem for problem in problems)


def test_production_with_a_short_signing_secret_is_a_problem() -> None:
    """A short secret is brute-forceable, so length is part of the rule."""

    problems = make_settings(
        environment="production", jwt_secret="short"
    ).validate_for_startup()

    assert any("32 characters" in problem for problem in problems)


def test_production_without_any_credentials_is_a_problem() -> None:
    """There has to be some way to reach Gemini."""

    problems = make_settings(
        environment="production",
        gemini_api_key="",
        project_id="",
        jwt_secret="x" * 40,
    ).validate_for_startup()

    assert any("GEMINI_API_KEY" in problem for problem in problems)


def test_production_logging_request_bodies_is_a_problem() -> None:
    """A body is the prompt or the screenshot, so logging bodies in production is refused."""

    problems = make_settings(
        environment="production",
        jwt_secret="x" * 40,
        project_id="a-project",
        log_request_bodies=True,
    ).validate_for_startup()

    assert any("LOG_REQUEST_BODIES" in problem for problem in problems)


def test_a_correct_production_configuration_has_no_problems() -> None:
    """The documented production shape produces a clean health check.

    Without this the refusals above only prove that almost everything is wrong.
    """

    production = make_settings(
        environment="production",
        jwt_secret="x" * 40,
        gemini_api_key="",
        project_id="a-project",
        require_authentication=True,
        vector_store="vertex",
        vector_search_index_id="1234567890",
    )

    assert production.validate_for_startup() == []


# ---------------------------------------------------------------- the awkward combinations


def test_authentication_on_with_no_secret_is_a_problem() -> None:
    """Requiring a token while having nothing to check it with would refuse every request."""

    problems = make_settings(require_authentication=True, jwt_secret="").validate_for_startup()

    assert any("REQUIRE_AUTHENTICATION" in problem for problem in problems)


def test_asking_for_vertex_without_an_index_is_a_problem() -> None:
    """The service would answer from general knowledge while claiming to be grounded."""

    problems = make_settings(
        vector_store="vertex", vector_search_index_id="", project_id="a-project"
    ).validate_for_startup()

    assert any("VECTOR_SEARCH_INDEX_ID" in problem for problem in problems)


def test_asking_for_vertex_without_a_project_is_a_problem() -> None:
    """There is no project to read the index from."""

    problems = make_settings(
        vector_store="vertex", vector_search_index_id="123", project_id=""
    ).validate_for_startup()

    assert any("PROJECT_ID" in problem for problem in problems)


# ---------------------------------------------------------------- the vector store choice


def test_the_default_store_is_in_process() -> None:
    """A first local run must work with nothing configured."""

    assert build_vector_database(make_settings()).name == "in-memory"


def test_a_project_alone_does_not_turn_on_a_vector_store() -> None:
    """The configuration that authenticates the model must not also change the store.

    This is the trap: the production shape sets PROJECT_ID for Vertex AI model access, so a store
    chosen by the presence of that value would come up unasked and fail on the first search.
    """

    assert build_vector_database(make_settings(project_id="a-project")).name == "in-memory"


def test_vertex_is_used_when_it_is_asked_for() -> None:
    """The explicit choice is honoured."""

    store = build_vector_database(
        make_settings(
            vector_store="vertex",
            vector_search_index_id="1234567890",
            project_id="a-project",
            region="europe-west4",
        )
    )

    assert store.name == "vertex-ai-vector-search"
    assert "europe-west4" in store.endpoint


def test_vertex_falls_back_rather_than_starting_broken() -> None:
    """A missing index leaves a usable service with an empty knowledge base.

    The alternative is refusing to start, which turns one wrong variable into an outage and gives
    the operator nothing but a stack trace.
    """

    store = build_vector_database(make_settings(vector_store="vertex"))

    assert store.name == "in-memory"


# ---------------------------------------------------------------- bounds


def test_an_image_limit_below_a_kilobyte_is_refused() -> None:
    """A limit that small would refuse every real screenshot."""

    with pytest.raises(ValueError):
        Settings(max_image_bytes=100)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("gemini_max_tokens", 0),
        ("gemini_max_tokens", 40000),
        ("gemini_temperature", 2.5),
        ("gemini_timeout_seconds", 0),
        ("gemini_timeout_seconds", 5000),
        ("rate_limit_chat_per_minute", 0),
        ("rag_top_k", 51),
        ("rag_top_k", 0),
    ],
)
def test_a_nonsensical_bound_is_refused(field: str, value: object) -> None:
    """Bounds are checked at load time rather than becoming a runtime surprise.

    The field names are the Python ones, not the environment aliases: this is the model, and a
    bound that is only enforced in production would fail on a laptop and pass in Cloud Run.
    """

    with pytest.raises(ValueError):
        Settings(**{field: value})


def test_a_negative_temperature_is_refused() -> None:
    """A negative temperature is not a tuning choice, it is a mistake."""

    with pytest.raises(ValueError):
        Settings(gemini_temperature=-0.5)


def test_the_similarity_floor_is_a_fraction() -> None:
    """A floor above 1 would reject everything and a negative one would accept everything."""

    with pytest.raises(ValueError):
        Settings(rag_minimum_similarity=1.5)


def test_settings_are_read_from_the_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    """An operator setting a variable in Cloud Run gets what they asked for."""

    monkeypatch.setenv("MODEL_NAME", "gemini-2.5-flash")
    monkeypatch.setenv("RATE_LIMIT_VISION_PER_MINUTE", "2")

    read = Settings(_env_file=None)

    assert read.gemini_model_name == "gemini-2.5-flash"
    assert read.rate_limit_vision_per_minute == 2


def test_a_bad_environment_value_names_the_variable(monkeypatch: pytest.MonkeyPatch) -> None:
    """The error has to say which variable, or an operator is left guessing at the dashboard."""

    monkeypatch.setenv("RATE_LIMIT_VISION_PER_MINUTE", "not a number")

    with pytest.raises(ValueError) as failure:
        Settings(_env_file=None)

    assert "RATE_LIMIT_VISION_PER_MINUTE" in str(failure.value)


def test_the_settings_are_cached_so_a_rebuild_reuses_them() -> None:
    """Reading the environment once per process is what makes the values stable.

    Settings read on every request would change under a revision update and produce answers that
    differ for reasons nobody can see.
    """

    assert get_settings() is get_settings()
