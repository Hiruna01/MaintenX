"""
Endpoint tests. TestClient is used as a context manager so the lifespan runs and the
graph is actually built — otherwise /run would fail on a missing app.state.graph.
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from main import app
from schemas import MAX_QUESTIONS, AgentStatus, PlanAgent, RunResponse

# conftest.py sets AGENT_SHARED_SECRET to this for the whole run.
SECRET = "test-shared-secret"


@pytest.fixture
def client():
    """A caller holding the shared secret — the API, as far as /run is concerned."""
    with TestClient(app) as test_client:
        test_client.headers.update({"X-Agent-Secret": SECRET})
        yield test_client


@pytest.fixture
def stranger():
    """A caller that can reach the port but does not hold the secret."""
    with TestClient(app) as test_client:
        yield test_client


FRESH_REPORT = {
    "workflow_id": 7,
    "description": "Water leaking from the ceiling near the lab entrance.",
    "room_id": 1,
}


def test_run_without_the_secret_is_refused(stranger):
    """Only the API may start an agent run — not anything else that can reach this port."""
    response = stranger.post("/run", json=FRESH_REPORT)

    assert response.status_code == 401


def test_run_with_the_wrong_secret_is_refused(stranger):
    response = stranger.post("/run", json=FRESH_REPORT, headers={"X-Agent-Secret": "guessed"})

    assert response.status_code == 401


def test_a_refused_caller_is_told_nothing_about_the_body(stranger):
    """401 before 422: a caller without the secret never learns what a valid body looks like."""
    response = stranger.post("/run", json={"not": "a run request"})

    assert response.status_code == 401
    assert "workflow_id" not in response.text


def test_health_needs_no_secret(stranger):
    assert stranger.get("/health").status_code == 200


def test_a_fresh_run_is_planned_first_and_carries_the_plan(client):
    """
    The planner runs before anything else on a fresh report, and its plan travels in `plan`
    for the API to check and store. The stub plan includes the clarifier, so the clarifier's
    fields are at the top level as always.
    """
    response = client.post("/run", json=FRESH_REPORT)

    assert response.status_code == 200
    body = RunResponse.model_validate(response.json())

    assert body.plan is not None
    assert body.plan.agent == "planner"
    assert body.plan.status is AgentStatus.ok
    assert [step.agent for step in body.plan.output.steps] == [
        PlanAgent.clarifier,
        PlanAgent.diagnostic,
        PlanAgent.strategist,
    ]
    assert body.plan.tool_calls == []
    assert body.agent == "clarifier"


def test_every_agent_reports_its_attempts_and_its_own_time(client):
    response = client.post(
        "/run",
        json={**FRESH_REPORT, "clarification_answers": [
            {"question_text": "Is it still leaking?", "answer_text": "Yes"},
        ]},
    )

    raw = response.json()

    # The stub's first reply always validates: one attempt each, no retry.
    assert raw["diagnosis"]["attempts"] == 1
    assert raw["strategy"]["attempts"] == 1
    assert isinstance(raw["diagnosis"]["duration_ms"], int)
    assert isinstance(raw["strategy"]["duration_ms"], int)
    # The stub reports no tokens, and none is null on the wire — never a made-up zero.
    assert raw["usage"] is None
    assert raw["diagnosis"]["usage"] is None
    assert raw["strategy"]["usage"] is None
    # A resumed run follows the plan the API already stored: it is not planned again.
    assert raw["plan"] is None


def test_health_reports_stub_mode(client):
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "healthy"
    assert body["stub_mode"] is True


def test_health_never_leaks_a_credential(client):
    """A health endpoint is the classic accidental credential leak."""
    body = client.get("/health").text

    assert "test-shared-secret" not in body
    assert "test-key-never-used" not in body
    assert "api_key" not in body
    assert "secret" not in body


def test_run_returns_a_valid_response(client):
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Water leaking from the ceiling near the lab entrance.",
            "room_id": 1,
            "building_id": 1,
        },
    )

    assert response.status_code == 200

    body = RunResponse.model_validate(response.json())
    assert body.workflow_id == 7
    assert body.agent == "clarifier"
    assert body.status is AgentStatus.ok
    assert len(body.output.questions) <= MAX_QUESTIONS


def test_run_rejects_a_body_it_does_not_recognise(client):
    """extra="forbid" on RunRequest means a stray field is a 422, not silently ignored."""
    response = client.post(
        "/run",
        json={"workflow_id": 7, "description": "Leak.", "conversation_history": ["hi"]},
    )

    assert response.status_code == 422


def test_run_requires_a_description(client):
    response = client.post("/run", json={"workflow_id": 7, "description": ""})

    assert response.status_code == 422


def test_a_report_the_clarifier_asks_about_stops_at_the_clarifier(client):
    """
    Human pause 1. The stub clarifier asks two questions, so the run ends with them: no
    diagnosis and no proposal, because both would be made without the detail the clarifier
    just said it needed. The clarifier's fields are exactly where the API reads them.
    """
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Lecture Hall A projector keeps cutting out mid lecture.",
            "room_id": 1,
            "asset_id": 1,
        },
    )

    assert response.status_code == 200

    body = RunResponse.model_validate(response.json())
    assert body.agent == "clarifier"
    assert body.status is AgentStatus.ok
    assert 0 < len(body.output.questions) <= MAX_QUESTIONS
    assert body.diagnosis is None and body.strategy is None


def test_a_resumed_run_returns_the_diagnosis_and_the_proposal_without_asking_again(client):
    """
    The reporter has answered, so the run starts at the diagnostic. The clarifier did not
    run: the top-level fields are the diagnostic's, with an empty question list, and the
    diagnosis and the proposal sit where they always do. The proposal carries no approval
    anywhere — the API decides that.
    """
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Lecture Hall A projector keeps cutting out mid lecture.",
            "room_id": 1,
            "asset_id": 1,
            "clarification_answers": [
                {"question_text": "Is the equipment completely unresponsive?", "answer_text": "No"},
            ],
        },
    )

    assert response.status_code == 200
    raw = response.json()

    body = RunResponse.model_validate(raw)
    assert body.agent == "diagnostic"
    assert body.status is AgentStatus.ok
    assert body.output.questions == []

    assert body.diagnosis is not None
    assert body.diagnosis.agent == "diagnostic"
    assert body.diagnosis.status is AgentStatus.ok
    assert [c.tool for c in body.diagnosis.tool_calls] == [
        "get_asset",
        "get_asset_service_history",
        "get_related_open_reports",
    ]

    assert body.strategy is not None
    assert body.strategy.agent == "strategist"
    assert body.strategy.status is AgentStatus.ok
    assert [c.tool for c in body.strategy.tool_calls] == [
        "get_asset",
        "get_asset_service_history",
        "get_open_work_orders",
    ]

    # No clarifier tool call anywhere: get_room is the clarifier's alone.
    assert "get_room" not in {c.tool for c in body.diagnosis.tool_calls + body.strategy.tool_calls}

    # Money as a JSON number on the wire, for the API's decimal to read.
    assert isinstance(raw["strategy"]["output"]["estimated_cost"], float)
    assert "approved" not in raw["strategy"]["output"]


def test_a_reopened_run_is_the_diagnostic_again_with_fresh_lookups(client):
    """
    A repair that did not hold. The clarifier does not run; the diagnostic and the strategist
    do, and each makes its own tool calls on this run — nothing is carried over from the
    first one, which is what lets the service record the repair appended be read.
    """
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Lecture Hall A projector keeps cutting out mid lecture.",
            "room_id": 1,
            "asset_id": 1,
            "reopened": True,
        },
    )

    assert response.status_code == 200
    body = RunResponse.model_validate(response.json())

    assert body.agent == "diagnostic"
    assert body.output.questions == []
    assert body.diagnosis is not None and body.diagnosis.status is AgentStatus.ok
    assert [c.tool for c in body.diagnosis.tool_calls] == [
        "get_asset",
        "get_asset_service_history",
        "get_related_open_reports",
    ]
    assert body.strategy is not None


def test_run_rejects_a_reopened_flag_that_is_not_a_boolean(client):
    response = client.post(
        "/run",
        json={"workflow_id": 7, "description": "Projector cutting out.", "reopened": "the fault is back"},
    )

    assert response.status_code == 422


def test_a_revision_run_is_the_strategist_alone_at_the_top_level(client):
    """
    A manager sent the proposal back. Only the strategist runs, so the top-level fields are
    its own — the same way a resumed run's are the diagnostic's — with no diagnosis beside
    it and nobody asked anything. The API reads the proposal from `strategy`, as always.
    """
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "asset_id": 1,
            "revision_note": "Too expensive this term - look at a repair first.",
            "revision_work_order_id": 57,
        },
    )

    assert response.status_code == 200
    body = RunResponse.model_validate(response.json())

    assert body.agent == "strategist"
    assert body.status is AgentStatus.ok
    assert body.output.questions == []
    assert body.plan is None and body.diagnosis is None
    assert body.strategy is not None and body.strategy.status is AgentStatus.ok
    assert body.duration_ms == body.strategy.duration_ms


def test_run_rejects_a_revision_work_order_id_that_is_not_an_id(client):
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "revision_note": "Price a repair first.",
            "revision_work_order_id": 0,
        },
    )

    assert response.status_code == 422


def test_run_accepts_earlier_clarification_answers(client):
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "asset_id": 1,
            "clarification_answers": [
                {"question_text": "Is the power light on?", "answer_text": "Yes"},
            ],
        },
    )

    assert response.status_code == 200


def test_run_rejects_an_answer_longer_than_the_apis_own_cap(client):
    """100 characters, the same as ClarificationAnswer.AnswerText on the API side."""
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "clarification_answers": [
                {"question_text": "Which input?", "answer_text": "x" * 101},
            ],
        },
    )

    assert response.status_code == 422


def test_a_verification_run_fills_verification_and_runs_nothing_else(client):
    """
    A completed repair, not a report: the verification agent answers, and the report
    pipeline does not run — no questions, no diagnosis, no proposal. The top-level fields
    describe the run, with an empty question list because nobody was asked anything.
    """
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Lecture Hall A projector keeps cutting out about ten minutes into every lecture.",
            "verification": {
                "work_order_id": 3,
                "reporter_confirmed": False,
                "reporter_comment": "Cut out twice again this week. Same as before.",
            },
        },
    )

    assert response.status_code == 200
    raw = response.json()
    body = RunResponse.model_validate(raw)

    assert body.agent == "verification"
    assert body.output.questions == []
    assert body.diagnosis is None and body.strategy is None

    assert body.verification is not None
    assert body.verification.status is AgentStatus.ok
    assert [c.tool for c in body.verification.tool_calls] == [
        "get_work_order",
        "get_asset_service_history",
        "get_related_open_reports",
    ]
    assert set(raw["verification"]["output"]) == {"outcome", "confidence", "reason", "evidence"}


def test_a_verification_comment_longer_than_the_apis_cap_is_refused(client):
    """300 characters, the same as ReporterConfirmationDto.Comment on the API side."""
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "verification": {"work_order_id": 3, "reporter_comment": "x" * 301},
        },
    )

    assert response.status_code == 422
