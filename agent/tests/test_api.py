"""
Endpoint tests. TestClient is used as a context manager so the lifespan runs and the
graph is actually built — otherwise /run would fail on a missing app.state.graph.
"""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from main import app
from schemas import MAX_QUESTIONS, AgentStatus, RunResponse


@pytest.fixture
def client():
    with TestClient(app) as test_client:
        yield test_client


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


def test_run_accepts_a_managers_revision_note(client):
    response = client.post(
        "/run",
        json={
            "workflow_id": 7,
            "description": "Projector cutting out.",
            "asset_id": 1,
            "revision_note": "Too expensive this term - look at a repair first.",
        },
    )

    assert response.status_code == 200


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
