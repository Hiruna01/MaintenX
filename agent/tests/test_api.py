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
