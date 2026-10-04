"""
Token usage — what the provider REPORTED, carried from the reply to the agent's envelope.

Usage is observability, like `attempts`: the API stores it on the agent's step and works out
cost from it in C#. So the rules here are about not inventing anything — a reply without a
`usage` block is None, never zero, a malformed one is ignored rather than raised on, and the
one retry's tokens are added to the first attempt's, because both were spent.
"""

from __future__ import annotations

import json

import httpx
import pytest

import llm_client
from agents.planner import PlannerAgent
from conftest import NOT_JSON_AT_ALL, VALID_TWO_QUESTIONS, ScriptedResponder
from llm_client import LlmClient, LlmReply
from schemas import AgentStatus, ClarifierOutput, RunRequest, TokenUsage
from test_planner import WITHOUT_CLARIFIER, _NoToolsAllowed


async def _complete(client: LlmClient):
    return await client.complete_json(system="system", user="user", schema=ClarifierOutput)


def _provider_replying(monkeypatch, body: dict) -> None:
    """
    Points the real HTTP responder at an in-process transport that answers with `body`.
    No socket is opened, so the suite's network block still holds.
    """
    real_client = httpx.AsyncClient

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json=body)

    monkeypatch.setattr(
        llm_client.httpx,
        "AsyncClient",
        lambda **kwargs: real_client(transport=httpx.MockTransport(handler), **kwargs),
    )


def _chat_completion(content: str, usage=None) -> dict:
    body = {"choices": [{"message": {"role": "assistant", "content": content}}]}
    if usage is not None:
        body["usage"] = usage
    return body


# --- Reading the provider's usage block ---------------------------------------------


async def test_the_real_responder_reads_the_providers_usage(live_settings, monkeypatch):
    _provider_replying(
        monkeypatch,
        _chat_completion(
            VALID_TWO_QUESTIONS,
            {"prompt_tokens": 812, "completion_tokens": 95, "total_tokens": 907},
        ),
    )

    result = await _complete(LlmClient(live_settings))

    assert result.ok
    assert result.usage == TokenUsage(prompt_tokens=812, completion_tokens=95)


@pytest.mark.parametrize(
    "usage",
    [
        None,
        {"prompt_tokens": 812},
        {"prompt_tokens": "812", "completion_tokens": 95},
        {"prompt_tokens": -1, "completion_tokens": 95},
        {"prompt_tokens": True, "completion_tokens": 95},
        "812 tokens",
    ],
    ids=["absent", "half", "a-string", "negative", "a-bool", "not-an-object"],
)
async def test_a_missing_or_malformed_usage_block_is_none_and_the_reply_still_counts(
    live_settings, monkeypatch, usage
):
    body = _chat_completion(VALID_TWO_QUESTIONS)
    if usage is not None:
        body["usage"] = usage
    _provider_replying(monkeypatch, body)

    result = await _complete(LlmClient(live_settings))

    # Usage is observability: a provider reporting it oddly must not cost us the answer.
    assert result.ok
    assert result.usage is None


async def test_stub_mode_reports_no_usage_not_zero(settings):
    result = await _complete(LlmClient(settings))

    assert result.ok
    assert result.usage is None


# --- The retry ------------------------------------------------------------------------


async def test_the_retrys_tokens_are_added_to_the_first_attempts(live_settings):
    responder = ScriptedResponder(
        LlmReply(NOT_JSON_AT_ALL, TokenUsage(prompt_tokens=500, completion_tokens=40)),
        LlmReply(VALID_TWO_QUESTIONS, TokenUsage(prompt_tokens=620, completion_tokens=90)),
    )

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.ok
    assert result.attempts == 2
    assert result.usage == TokenUsage(prompt_tokens=1120, completion_tokens=130)


async def test_a_safe_failure_still_reports_the_tokens_it_spent(live_settings):
    responder = ScriptedResponder(
        LlmReply(NOT_JSON_AT_ALL, TokenUsage(prompt_tokens=500, completion_tokens=40)),
    )

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.ok is False
    assert result.usage == TokenUsage(prompt_tokens=1000, completion_tokens=80)


async def test_an_attempt_without_usage_adds_nothing_to_the_one_that_had_it(live_settings):
    responder = ScriptedResponder(
        LlmReply(NOT_JSON_AT_ALL, None),
        LlmReply(VALID_TWO_QUESTIONS, TokenUsage(prompt_tokens=620, completion_tokens=90)),
    )

    result = await _complete(LlmClient(live_settings, responder=responder))

    assert result.usage == TokenUsage(prompt_tokens=620, completion_tokens=90)


async def test_a_plain_text_responder_still_works_and_reports_no_usage(live_settings):
    """Every existing scripted test hands back bare strings; that contract is unchanged."""
    result = await _complete(LlmClient(live_settings, responder=ScriptedResponder(VALID_TWO_QUESTIONS)))

    assert result.ok
    assert result.usage is None


# --- Into the agent's envelope --------------------------------------------------------


async def test_an_agent_puts_the_usage_on_its_envelope(settings):
    responder = ScriptedResponder(
        LlmReply(json.dumps(WITHOUT_CLARIFIER), TokenUsage(prompt_tokens=430, completion_tokens=60)),
    )
    agent = PlannerAgent(llm=LlmClient(settings, responder=responder), tools=_NoToolsAllowed(settings))

    result = await agent.run(RunRequest(workflow_id=3, description="Projector cuts out, nothing unsafe."))

    assert result.status is AgentStatus.ok
    assert result.usage == TokenUsage(prompt_tokens=430, completion_tokens=60)
    assert result.model_dump(mode="json")["usage"] == {"prompt_tokens": 430, "completion_tokens": 60}
