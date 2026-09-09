"""
ClarifierAgent — turns a thin maintenance report into at most two closed questions.

Owner: (assign one team member here)

## One round, on purpose

This agent runs exactly once per report. It receives the report text plus whatever asset
context it can look up, returns its questions, and is finished. There is no conversation
history parameter, no follow-up call, and no free-text field in its output — see the
ClarifierOutput docstring in schemas.py.

That is a hard design constraint, not a first iteration. The moment the clarifier can see
what it said last turn, the system has a chat interface in it, and this project does not
have one. If a report is still unclear after one round, a human picks it up.
"""

from __future__ import annotations

import json
import logging

from llm_client import LlmClient
from prompts import load_prompt, render_prompt
from schemas import (
    MAX_QUESTIONS,
    AgentStatus,
    ClarifierOutput,
    RunRequest,
    RunResponse,
    ToolCallOutcome,
)
from tools import ToolClient

logger = logging.getLogger(__name__)


class ClarifierAgent:
    """Asks for the one or two missing facts a technician would need before attending."""

    name = "clarifier"

    # THIS AGENT'S TOOL SUBSET, declared explicitly and locally.
    #
    # The API exposes more tools than this, and every future agent will declare its own
    # subset. The clarifier only needs to know where the fault is, so it gets the two
    # read-only location lookups and nothing else. Widening this is a code change in this
    # file, reviewed by this agent's owner — and even then the API's own hardcoded
    # allow-list still has the final say.
    ALLOWED_TOOLS: tuple[str, ...] = ("get_room", "get_building")

    SYSTEM_PROMPT = "clarifier_system.md"
    USER_PROMPT = "clarifier_user.md"

    def __init__(self, llm: LlmClient, tools: ToolClient) -> None:
        self._llm = llm
        self._tools = tools

    async def run(self, request: RunRequest) -> RunResponse:
        """
        Returns a well-formed response in every case. A provider outage or a model that
        cannot produce valid JSON yields an empty question list with status safe_failure,
        never an exception.
        """
        context, tool_calls = await self._gather_context(request)

        user_prompt = render_prompt(
            self.USER_PROMPT,
            context=json.dumps(context, indent=2),
            description=request.description,
            max_questions=MAX_QUESTIONS,
        )

        result = await self._llm.complete_json(
            system=load_prompt(self.SYSTEM_PROMPT),
            user=user_prompt,
            schema=ClarifierOutput,
        )

        if not result.ok or result.data is None:
            logger.warning(
                "Clarifier fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                result.error,
            )
            return RunResponse(
                workflow_id=request.workflow_id,
                agent=self.name,
                status=AgentStatus.safe_failure,
                # Asking nothing is always safe: the report simply proceeds unclarified.
                output=ClarifierOutput(questions=[]),
                error=result.error,
                tool_calls=tool_calls,
            )

        output = result.data
        assert isinstance(output, ClarifierOutput)

        return RunResponse(
            workflow_id=request.workflow_id,
            agent=self.name,
            status=AgentStatus.ok,
            output=output,
            tool_calls=tool_calls,
        )

    async def _gather_context(
        self, request: RunRequest
    ) -> tuple[dict[str, object], list[ToolCallOutcome]]:
        """
        Looks up whatever location context the request points at. Missing context is not
        an error — the agent just has less to go on and says so in the prompt.
        """
        context: dict[str, object] = {}
        calls: list[ToolCallOutcome] = []

        for tool_name, entity_id, key in (
            ("get_room", request.room_id, "room"),
            ("get_building", request.building_id, "building"),
        ):
            if entity_id is None:
                continue

            outcome = await self._tools.call(
                tool_name,
                workflow_id=request.workflow_id,
                entity_id=entity_id,
                agent_name=self.name,
                allowed_tools=self.ALLOWED_TOOLS,
            )
            calls.append(outcome)
            context[key] = outcome.result if outcome.found else "not available"

        if not context:
            context["note"] = "No asset or room context was supplied with this report."

        return context, calls
