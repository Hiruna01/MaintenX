"""
Live eval for the reopen golden case — the MODEL's behaviour when a repair did not hold and
the diagnostic runs a second time, against a real provider.

    RUN_LIVE_EVALS=1 pytest evals/test_reopen_live.py -v

The data is tests/reopen_cases.py, the same data tests/test_reopen.py proves reaches the
prompt. That test can say the temporary fix and the new report are IN the second prompt; only
a real model can say whether it reads them. Two provider calls, and they cost money.

What is asserted, and why each:

  * the second run's most likely cause is a COOLING fault, and names neither the cable nor
    the HDMI connection — the repair visit found the cable fine and the fan rattling, and a
    report since says it cut out hot and rattling again;
  * it is a DIFFERENT cause from the first run's — the first run's evidence is a cable
    replaced for the same symptom, and the case is built so that is where it points;
  * `recommended_next_action` is `repair` or `replace` — the cause is identified now and a
    technician has written "fan needs replacing"; `inspect` would ask for a look the repair
    visit already took, and `monitor` would leave a known failing fan in service;
  * it cites the repair visit by its date, so the change of mind stands on the new record.

If the FIRST run already names the fan, the "different" assertion fails. Read the reply
before calling that a model fault: it is the case's first-run evidence that is meant to point
at the cable, and a first diagnosis that sees past it is not wrong — but it is then not a
test of reading new evidence, and the case needs a clearer first history.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import pytest

from agents.diagnostic import DiagnosticAgent
from config import Settings
from llm_client import LlmClient
from schemas import AgentStatus, DiagnosticOutput, NextAction

# The case lives beside the offline tests, which evals/ does not otherwise import from.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tests"))

import reopen_cases as case  # noqa: E402


def _live_settings() -> Settings | None:
    if os.environ.get("RUN_LIVE_EVALS") != "1":
        return None

    settings = Settings()
    if settings.stub_mode or not (settings.llm_base_url and settings.llm_model):
        return None
    return settings


LIVE = _live_settings()

pytestmark = pytest.mark.skipif(
    LIVE is None,
    reason="Live eval: set RUN_LIVE_EVALS=1 with LLM_BASE_URL and LLM_MODEL configured.",
)

COOLING_TERMS = ("overheat", "heat", "thermal", "hot", "fan", "cooling", "temperature", "vent")
CONNECTION_TERMS = ("hdmi", "cable", "connection", "connector", "signal", "input")


def _category(cause: str) -> str:
    text = cause.lower()
    if any(term in text for term in CONNECTION_TERMS):
        return "connection"
    if any(term in text for term in COOLING_TERMS):
        return "cooling"
    return "other"


async def _first_and_second() -> tuple[DiagnosticOutput, DiagnosticOutput]:
    """One agent, two runs, the database changed between them — as in tests/test_reopen.py."""
    tools = case.ChangingTools(case.FIRST_RUN)
    agent = DiagnosticAgent(llm=LlmClient(LIVE), tools=tools)

    first = await agent.run(case.request(reopened=False))
    tools.results = case.SECOND_RUN
    second = await agent.run(case.request(reopened=True))

    for result in (first, second):
        assert result.status is AgentStatus.ok, f"the model produced no valid diagnosis: {result.error}"
        assert result.output is not None

    return first.output, second.output


async def test_a_reopened_repair_changes_the_diagnosis_to_the_evidence_since():
    first, second = await _first_and_second()

    first_primary = first.hypotheses[first.primary_hypothesis_index]
    second_primary = second.hypotheses[second.primary_hypothesis_index]
    replies = f"first: {first.model_dump()}\nsecond: {second.model_dump()}"

    assert _category(second_primary.cause) == "cooling", (
        f"expected a cooling cause after the repair visit, got {second_primary.cause!r}\n{replies}"
    )
    assert _category(first_primary.cause) != _category(second_primary.cause), (
        f"the second run did not change its mind — see the module docstring\n{replies}"
    )

    assert second.recommended_next_action in (NextAction.repair, NextAction.replace), (
        f"expected repair or replace for an identified failing fan\n{replies}"
    )

    cited = " ".join(second_primary.evidence)
    assert "2026-09-05" in cited, f"expected the repair visit cited by date, got {second_primary.evidence}"
