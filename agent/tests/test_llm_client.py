"""
Tests for the parse / validate / retry-once / safe-failure contract.

These are the tests that justify not using provider structured-output: the failure path
is ordinary code, so it can be driven directly.
"""

from __future__ import annotations

import pytest

from conftest import (
    MALFORMED_JSON,
    NOT_JSON_AT_ALL,
    SELECT_WITHOUT_OPTIONS,
    VALID_TWO_QUESTIONS,
    RaisingResponder,
    ScriptedResponder,
)
from llm_client import MAX_ATTEMPTS, LlmClient
from schemas import ClarifierOutput

SYSTEM = "system prompt"
USER = "user prompt"


async def _complete(client: LlmClient):
    return await client.complete_json(system=SYSTEM, user=USER, schema=ClarifierOutput)


# --- STUB_MODE ---------------------------------------------------------------


async def test_stub_mode_returns_valid_output_without_a_network_call(settings, monkeypatch):
    # If anything tried to open an HTTP connection this would blow up.
    def explode(*args, **kwargs):
        raise AssertionError("STUB_MODE must not make a network call")

    monkeypatch.setattr("httpx.AsyncClient", explode)

    result = await _complete(LlmClient(settings))

    assert result.ok
    assert isinstance(result.data, ClarifierOutput)
    assert result.attempts == 1
    assert result.error is None


async def test_stub_output_satisfies_the_schema_it_claims_to(settings):
    """The stub is validated by the same path as a real reply, so it cannot drift."""
    result = await _complete(LlmClient(settings))

    assert result.ok
    assert len(result.data.questions) <= 2


# --- Valid output ------------------------------------------------------------


async def test_valid_output_passes_schema_on_the_first_attempt(live_settings):
    responder = ScriptedResponder(VALID_TWO_QUESTIONS)

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.ok
    assert responder.calls == 1, "a valid first reply must not trigger a retry"
    assert result.attempts == 1
    assert len(result.data.questions) == 2


@pytest.mark.parametrize(
    "wrapped",
    [
        "```json\n" + VALID_TWO_QUESTIONS + "\n```",
        "```\n" + VALID_TWO_QUESTIONS + "\n```",
        "Here you go:\n" + VALID_TWO_QUESTIONS + "\nHope that helps!",
    ],
)
async def test_json_is_extracted_from_fences_and_chatter(live_settings, wrapped):
    """Models wrap JSON in fences and prose. That is not a validation failure."""
    responder = ScriptedResponder(wrapped)

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.ok
    assert responder.calls == 1


# --- Retry exactly once ------------------------------------------------------


@pytest.mark.parametrize(
    "bad_reply",
    [NOT_JSON_AT_ALL, MALFORMED_JSON, SELECT_WITHOUT_OPTIONS],
    ids=["no-json", "truncated-json", "schema-violation"],
)
async def test_malformed_output_triggers_exactly_one_retry(live_settings, bad_reply):
    responder = ScriptedResponder(bad_reply, VALID_TWO_QUESTIONS)

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert responder.calls == 2, "expected exactly one retry"
    assert result.ok
    assert result.attempts == 2


async def test_the_retry_carries_the_validation_error(live_settings):
    responder = ScriptedResponder(SELECT_WITHOUT_OPTIONS, VALID_TWO_QUESTIONS)

    await _complete(LlmClient(live_settings, responder=responder))

    first, second = responder.conversations
    assert len(second) > len(first), "the retry must extend the conversation"

    # The model's own bad reply, then the correction, are both appended.
    assert second[-2]["role"] == "assistant"
    assert second[-2]["content"] == SELECT_WITHOUT_OPTIONS

    correction = second[-1]["content"]
    assert second[-1]["role"] == "user"
    assert "options" in correction, "the correction must name what was wrong"


# --- Second failure -> safe failure ------------------------------------------


async def test_a_second_failure_returns_the_safe_failure_object(live_settings):
    responder = ScriptedResponder(NOT_JSON_AT_ALL)  # every reply is unusable

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert responder.calls == MAX_ATTEMPTS == 2, "must stop after one retry, not keep trying"
    assert result.ok is False
    assert result.data is None
    assert result.error
    assert result.attempts == 2


async def test_a_provider_exception_is_never_raised_into_the_caller(live_settings):
    responder = RaisingResponder(TimeoutError("provider did not respond"))

    # The point of this test is that it does not raise.
    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.ok is False
    assert result.data is None
    assert "TimeoutError" in result.error
    assert responder.calls == 2, "a transport failure is retried once, like a bad reply"
