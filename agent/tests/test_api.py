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


def test_run_returns_a_diagnosis_beside_the_clarifier_output(client):
    """
    Two agents, one response — and the clarifier's fields are exactly where they were.
    The API's AgentRunResponse reads agent, status and output by name, so the diagnosis
    has to be an addition to this contract, never a reinterpretation of it.
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
    assert len(body.output.questions) <= MAX_QUESTIONS

    assert body.diagnosis is not None
    assert body.diagnosis.agent == "diagnostic"
    assert body.diagnosis.status is AgentStatus.ok
    assert [c.tool for c in body.diagnosis.tool_calls] == [
        "get_asset",
        "get_asset_service_history",
        "get_related_open_reports",
    ]


def test_run_returns_the_strategists_proposal_beside_the_diagnosis(client):
    """
    Third agent, still an addition: the clarifier's fields and the diagnosis are where they
    were. The proposal carries no approval anywhere — the API decides that.
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
    raw = response.json()

    body = RunResponse.model_validate(raw)
    assert body.agent == "clarifier"
    assert body.diagnosis is not None and body.diagnosis.status is AgentStatus.ok

    assert body.strategy is not None
    assert body.strategy.agent == "strategist"
    assert body.strategy.status is AgentStatus.ok
    assert [c.tool for c in body.strategy.tool_calls] == [
        "get_asset",
        "get_asset_service_history",
        "get_open_work_orders",
    ]

    # Money as a JSON number on the wire, for the API's decimal to read.
    assert isinstance(raw["strategy"]["output"]["estimated_cost"], float)
    assert "approved" not in raw["strategy"]["output"]


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
