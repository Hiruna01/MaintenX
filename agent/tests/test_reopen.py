"""
The reopen golden case, structural half: what the CODE guarantees when a repair did not hold
and the diagnostic runs a second time.

  * the second run looks everything up AGAIN through the same three tools — nothing is
    remembered from the first — so the service record the repair appended and the report
    filed since both reach its prompt;
  * the first run's diagnosis does NOT reach the second prompt: no conversation history,
    so the second opinion is formed from the evidence, not anchored on the first;
  * each run returns its own result — the API appends each as its own AgentStep, which is
    what lets the two be read side by side.

Whether the model then names a DIFFERENT cause, and recommends repair or replace, is its
behaviour, and against a scripted reply a test of that would only check the script. That
half is evals/test_reopen_live.py, on exactly the same data (tests/reopen_cases.py).

Routing — a reopened run skips the clarifier — is pinned in test_graph.py and test_api.py.
"""

from __future__ import annotations

import json

import pytest
from pydantic import ValidationError

import reopen_cases as case
from agents.diagnostic import DiagnosticAgent
from conftest import ScriptedResponder
from llm_client import LlmClient
from schemas import AgentStatus, DiagnosticInput, DiagnosticOutput, RunRequest

BEGIN_DATA = "--- BEGIN DATA ---"
END_DATA = "--- END DATA ---"

# Two different scripted replies, so each run's output is tellable apart. Their CONTENT is
# not evidence of anything — a script says what it is told to.
FIRST_REPLY = json.dumps(
    {
        "hypotheses": [
            {
                "cause": "Intermittent HDMI connection at the lectern",
                "confidence": "medium",
                "evidence": ["2026-06-18: picture dropping out, hdmi cable split, replaced"],
            }
        ],
        "primary_hypothesis_index": 0,
        "recommended_next_action": "inspect",
        "reasoning_summary": "The same symptom was last fixed by replacing the cable.",
    }
)

SECOND_REPLY = json.dumps(
    {
        "hypotheses": [
            {
                "cause": "Thermal cut-out from a failing cooling fan",
                "confidence": "high",
                "evidence": [
                    "2026-09-05: unit v hot at exhaust, fan rattling, temporary fix",
                    "New report 2026-09-12: cut out again, rattling, hot vent air",
                ],
            }
        ],
        "primary_hypothesis_index": 0,
        "recommended_next_action": "repair",
        "reasoning_summary": "The cable was fine on the repair visit; the fan was not.",
    }
)


def _data_block(prompt: str) -> dict:
    lines = prompt.splitlines()
    assert lines.count(BEGIN_DATA) == 1 and lines.count(END_DATA) == 1
    start, end = lines.index(BEGIN_DATA), lines.index(END_DATA)
    return json.loads("\n".join(lines[start + 1 : end]))


async def _two_runs(settings):
    """
    ONE agent, run twice, with the database changing in between — the way the process
    actually lives: the agent is built once at startup and serves every /run.
    """
    responder = ScriptedResponder(FIRST_REPLY, SECOND_REPLY)
    tools = case.ChangingTools(case.FIRST_RUN)
    agent = DiagnosticAgent(llm=LlmClient(settings, responder=responder), tools=tools)

    first = await agent.run(case.request(reopened=False))

    # The repair is completed (a ServiceRecord appended) and the fault is reported again.
    tools.results = case.SECOND_RUN

    second = await agent.run(case.request(reopened=True))

    prompts = [conversation[1]["content"] for conversation in responder.conversations]
    return first, second, prompts, tools


async def test_the_second_run_looks_everything_up_again(settings):
    """Six tool calls, not three: nothing from the first run's lookups is reused."""
    _, _, _, tools = await _two_runs(settings)

    assert tools.asked == [(tool, case.ASSET_ID) for tool in DiagnosticAgent.ALLOWED_TOOLS] * 2


async def test_the_second_prompt_carries_the_repair_and_the_report_filed_since(settings):
    _, _, prompts, _ = await _two_runs(settings)
    first, second = _data_block(prompts[0]), _data_block(prompts[1])

    # Before the repair: two visits, neither a temporary fix; one open report, the original.
    assert [r["serviced_on"] for r in first["service_history_newest_first"]] == ["2026-06-18", "2026-02-10"]
    assert "TemporaryFix" not in {r["outcome"] for r in first["service_history_newest_first"]}
    assert [r["report_id"] for r in first["other_open_reports"]] == [60]

    # After: the repair's own record LEADS the history, note verbatim and outcome intact —
    # the admission "temporary fix - fan needs replacing" is the evidence the second run is for.
    newest = second["service_history_newest_first"][0]
    assert newest == {
        "serviced_on": "2026-09-05",
        "outcome": "TemporaryFix",
        "technician_note": case.TEMPORARY_FIX_NOTE,
    }
    assert [r["serviced_on"] for r in second["service_history_newest_first"]] == [
        "2026-09-05",
        "2026-06-18",
        "2026-02-10",
    ]

    # And the report filed after the repair, verbatim, with a date the model can put after it.
    new_report = second["other_open_reports"][0]
    assert new_report["report_id"] == 61
    assert new_report["description"] == case.NEW_REPORT_DESCRIPTION
    assert new_report["reported_at"] > newest["serviced_on"]

    # Same report, same asset, nothing missing.
    assert second["report"]["description"] == first["report"]["description"] == case.DESCRIPTION
    assert second["notes"] == []


async def test_the_first_diagnosis_does_not_reach_the_second_prompt(settings):
    """
    No conversation history, across runs as well as within one. Shown its own earlier answer,
    the model would be asked to agree or disagree with itself — and a second opinion anchored
    on the first is not a second opinion.
    """
    _, _, prompts, _ = await _two_runs(settings)

    first_cause = json.loads(FIRST_REPLY)["hypotheses"][0]["cause"]
    assert first_cause not in prompts[1]
    assert "reasoning_summary" not in _data_block(prompts[1])
    assert set(_data_block(prompts[1])) == set(_data_block(prompts[0]))


async def test_each_run_returns_its_own_diagnosis(settings):
    """
    Two results, neither overwriting the other. The API records each as its own AgentStep
    (WorkflowRunnerTests pins that side), so the React workflow view can compare them.
    """
    first, second, _, _ = await _two_runs(settings)

    assert first.status is AgentStatus.ok and second.status is AgentStatus.ok
    assert first.output == DiagnosticOutput.model_validate_json(FIRST_REPLY)
    assert second.output == DiagnosticOutput.model_validate_json(SECOND_REPLY)
    assert first.output != second.output

    # The contract is unchanged by a reopen: the same four fields, nothing added for it.
    assert set(DiagnosticOutput.model_fields) == {
        "hypotheses",
        "primary_hypothesis_index",
        "recommended_next_action",
        "reasoning_summary",
    }


def test_the_reopen_flag_routes_and_is_never_shown_to_the_diagnostic():
    """
    `reopened` is on RunRequest for graph.py to route by. DiagnosticInput is the diagnostic's
    whole view of the request, and it has no such field — what changed is in the data.
    """
    projected = DiagnosticInput.from_run_request(case.request(reopened=True))

    assert "reopened" not in DiagnosticInput.model_fields
    assert projected == DiagnosticInput.from_run_request(case.request(reopened=False))

    with pytest.raises(ValidationError):
        RunRequest(workflow_id=1, description="x", reopened=True, previous_diagnosis={"cause": "fan"})
