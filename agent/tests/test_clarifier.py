"""
Tests for ClarifierAgent.

The two-question ceiling and the no-chatbot constraint are checked here rather than left
to code review, because both are the kind of rule that quietly erodes.
"""

from __future__ import annotations

import pytest

from agents.clarifier import ClarifierAgent
from conftest import (
    FREE_TEXT_MESSAGE_FIELD,
    NOT_JSON_AT_ALL,
    THREE_QUESTIONS,
    VALID_EMPTY,
    VALID_TWO_QUESTIONS,
    ScriptedResponder,
)
from llm_client import LlmClient
from schemas import MAX_QUESTIONS, AgentStatus, ClarifierOutput, RunRequest
from tools import ToolClient

REQUEST = RunRequest(
    workflow_id=12,
    description="The projector in the lecture hall keeps cutting out mid-class.",
    room_id=1,
    building_id=1,
)


def _agent(settings, responder=None) -> ClarifierAgent:
    return ClarifierAgent(
        llm=LlmClient(settings, responder=responder),
        tools=ToolClient(settings),
    )


# --- Happy path --------------------------------------------------------------


async def test_stub_run_produces_valid_output(settings):
    response = await _agent(settings).run(REQUEST)

    assert response.status is AgentStatus.ok
    assert response.workflow_id == 12
    assert response.agent == "clarifier"
    assert response.error is None
    assert len(response.output.questions) <= MAX_QUESTIONS


async def test_asking_nothing_is_a_valid_answer(settings):
    response = await _agent(settings, ScriptedResponder(VALID_EMPTY)).run(REQUEST)

    assert response.status is AgentStatus.ok
    assert response.output.questions == []


# --- The two-question ceiling ------------------------------------------------


@pytest.mark.parametrize(
    "reply",
    [VALID_TWO_QUESTIONS, VALID_EMPTY, THREE_QUESTIONS, NOT_JSON_AT_ALL, FREE_TEXT_MESSAGE_FIELD],
    ids=["two", "none", "three", "garbage", "chatty"],
)
async def test_clarifier_never_returns_more_than_two_questions(settings, reply):
    """Whatever the model says, the caller never sees a third question."""
    response = await _agent(settings, ScriptedResponder(reply)).run(REQUEST)

    assert len(response.output.questions) <= MAX_QUESTIONS


async def test_three_questions_is_rejected_rather_than_silently_truncated(settings):
    responder = ScriptedResponder(THREE_QUESTIONS)

    response = await _agent(settings, responder).run(REQUEST)

    # Rejected by the schema, retried once, then safe failure — not quietly trimmed to
    # two, which would hide a misbehaving prompt.
    assert responder.calls == 2
    assert response.status is AgentStatus.safe_failure
    assert response.output.questions == []


# --- Safe failure ------------------------------------------------------------


async def test_safe_failure_is_well_formed_and_not_an_exception(settings):
    response = await _agent(settings, ScriptedResponder(NOT_JSON_AT_ALL)).run(REQUEST)

    assert response.status is AgentStatus.safe_failure
    assert response.output.questions == []
    assert response.error, "a safe failure must say what went wrong"
    assert response.workflow_id == 12


# --- The no-chatbot constraint ----------------------------------------------


def test_clarifier_output_has_no_free_text_or_history_field():
    """
    Guards the hard design constraint. If someone adds a message or history field to
    ClarifierOutput, this fails and they have to justify it in review.
    """
    banned = {"message", "reply", "response", "history", "messages", "follow_up", "next_turn"}

    assert banned.isdisjoint(ClarifierOutput.model_fields), (
        "ClarifierOutput must stay questions-only; this system has no chat interface."
    )
    assert set(ClarifierOutput.model_fields) == {"questions"}


async def test_a_chatty_model_reply_does_not_become_a_message(settings):
    """A model that tries to converse gets its prose dropped, not passed through."""
    response = await _agent(settings, ScriptedResponder(FREE_TEXT_MESSAGE_FIELD)).run(REQUEST)

    assert not hasattr(response.output, "message")
    assert response.status is AgentStatus.safe_failure


# --- Tool subset -------------------------------------------------------------


def test_clarifier_declares_an_explicit_tool_subset():
    assert ClarifierAgent.ALLOWED_TOOLS == ("get_room", "get_building")


async def test_a_tool_outside_the_subset_is_refused_without_leaving_the_process(settings):
    outcome = await ToolClient(settings).call(
        "delete_building",
        workflow_id=12,
        entity_id=1,
        agent_name="clarifier",
        allowed_tools=ClarifierAgent.ALLOWED_TOOLS,
    )

    assert outcome.found is False
    assert "not allowed" in outcome.error


async def test_context_is_fetched_through_tools(settings):
    response = await _agent(settings).run(REQUEST)

    assert [call.tool for call in response.tool_calls] == ["get_room", "get_building"]
    assert all(call.found for call in response.tool_calls)


async def test_a_report_with_no_location_makes_no_tool_calls(settings):
    bare = RunRequest(workflow_id=12, description="Something is broken.")

    response = await _agent(settings).run(bare)

    assert response.tool_calls == []
    assert response.status is AgentStatus.ok
