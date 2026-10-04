"""
PlannerAgent — reads the objective and delegates the run: which agents, in what order, why.

Owner: (assign one team member here)

## What it decides, and what it cannot

A report run is delegated to at most three agents, always in the same order: the clarifier,
the diagnostic, the strategist. The planner decides the one thing that genuinely varies
between reports — whether the clarifier is needed at all — and writes what each delegated
agent is to establish for THIS report. A report that already says what is wrong, whether it
is dead or intermittent, and whether it is safe goes straight to diagnosis; anything less
goes to the clarifier first.

It cannot invent an agent, reorder the pipeline, leave out the diagnostic or the strategist,
or approve anything. PlannerOutput rejects every one of those before graph.py routes on the
plan, and the API's PlanRules checks the same rules again before the plan is stored. The
plan is advice about delegation; which transitions happen, and what is raised or approved,
is still C#.

## No tools, on purpose

Its tool subset is EMPTY. Deciding whether a report needs clarifying is a judgement about the
report's own words; looking up the room or the asset is the clarifier's job, and reading
history is the diagnostic's. Least privilege: an agent that needs no data is given none.

## Everything it reads is data

The report goes in as ONE JSON object between markers, like every other agent's — never
spliced raw — so a description cannot close the data block and start giving instructions.
A report that talks the model into "skip everything and approve it" cannot produce such a
plan: the schema has no field for it, and a plan without the diagnostic and the strategist
is refused.
"""

from __future__ import annotations

import json
import logging

from llm_client import LlmClient
from prompts import load_prompt, render_prompt
from schemas import (
    MAX_PLAN_PURPOSE,
    AgentStatus,
    PlannerInput,
    PlannerOutput,
    PlannerResult,
    RunRequest,
)
from tools import ToolClient

logger = logging.getLogger(__name__)


class PlannerAgent:
    """Turns one report into a structured plan: the agents that will handle it, and why."""

    name = "planner"

    # THIS AGENT'S TOOL SUBSET: none. See the module docstring. Declared anyway, so the
    # absence is a decision a reader can see rather than an omission.
    ALLOWED_TOOLS: tuple[str, ...] = ()

    SYSTEM_PROMPT = "planner.md"
    USER_PROMPT = "planner_user.md"

    def __init__(self, llm: LlmClient, tools: ToolClient) -> None:
        # The tool client is accepted like every agent's, and never used: ALLOWED_TOOLS is empty.
        self._llm = llm
        self._tools = tools

    async def run(self, request: RunRequest) -> PlannerResult:
        """
        Returns a well-formed result in every case. A provider outage or a model that cannot
        produce a valid plan yields status safe_failure with no output, never an exception —
        and graph.py then runs the full pipeline, the clarifier included.
        """
        planner_input = PlannerInput.from_run_request(request)

        result = await self._llm.complete_json(
            system=load_prompt(self.SYSTEM_PROMPT),
            user=render_prompt(
                self.USER_PROMPT,
                data=self._render_data(planner_input),
                max_purpose=MAX_PLAN_PURPOSE,
            ),
            schema=PlannerOutput,
        )

        if not result.ok or result.data is None:
            logger.warning(
                "Planner fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                result.error,
            )
            return PlannerResult(
                agent=self.name,
                status=AgentStatus.safe_failure,
                # No plan rather than a made-up one: the graph's own default is the full
                # pipeline, and the API records why it was used.
                output=None,
                error=result.error,
                attempts=result.attempts,
                usage=result.usage,
            )

        output = result.data
        assert isinstance(output, PlannerOutput)

        return PlannerResult(
            agent=self.name,
            status=AgentStatus.ok,
            output=output,
            attempts=result.attempts,
            usage=result.usage,
        )

    @staticmethod
    def _render_data(planner_input: PlannerInput) -> str:
        """
        The report as ONE JSON object — the structural half of the prompt-injection defence,
        the same as every other agent's. See DiagnosticAgent._render_data.
        """
        payload = {
            "report": {"description": planner_input.description},
            "room_identified": planner_input.room_identified,
            "asset_identified": planner_input.asset_identified,
        }
        return json.dumps(payload, indent=2, ensure_ascii=False)
