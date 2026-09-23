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

## Knowing what not to ask

A question the report already answers, or the room or asset record already answers, is a
question the reporter should never see. So the agent looks up the room and the asset
first, and the prompt tells the model that anything in those records is known. Zero
questions is a correct and common answer.

## Everything it reads is data

The report is typed by a member of the public. It goes into the prompt as ONE JSON object
between markers, together with the looked-up context, never spliced in as raw text — the
same defence as DiagnosticAgent. json.dumps escapes every newline, so a description cannot
put a closing marker on a line of its own and start writing instructions. The two-question
ceiling does not depend on the model at all: ClarifierOutput rejects a third question, so
a report that talks the model into "ask ten questions" produces a safe failure, never a
long form. What the model does with the prompt is measured in agent/evals/.
"""

from __future__ import annotations

import json
import logging
from typing import Any

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
    # Where the fault is (get_room) and what kind of equipment it is (get_asset) — enough
    # to avoid asking either, and to ask about the right machine. Deliberately NOT
    # get_asset_service_history or get_related_open_reports: reading repair history is
    # DiagnosticAgent's job, and a clarifier that could see it would start diagnosing
    # instead of asking. get_asset is the one tool the two subsets share.
    #
    # Widening this is a code change in this file, reviewed by this agent's owner — and
    # even then the API's own hardcoded allow-list still has the final say.
    ALLOWED_TOOLS: tuple[str, ...] = ("get_room", "get_asset")

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
            data=self._render_data(request.description, context),
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
    ) -> tuple[dict[str, Any], list[ToolCallOutcome]]:
        """
        Looks up the room and the asset the request points at. Missing context is not an
        error — the agent just has less to go on, and `notes` tells the model so.

        building_id is not looked up: where in the estate a room sits changes nothing a
        reporter could be asked, and the tool is not in this agent's subset.
        """
        context: dict[str, Any] = {"room": None, "asset": None, "notes": []}
        calls: list[ToolCallOutcome] = []

        if request.room_id is not None:
            room = await self._call("get_room", request.workflow_id, request.room_id)
            calls.append(room)
            if room.found:
                context["room"] = _room_facts(room.result)
            else:
                context["notes"].append("The room could not be looked up.")
        else:
            context["notes"].append("No room was supplied with this report.")

        if request.asset_id is not None:
            asset = await self._call("get_asset", request.workflow_id, request.asset_id)
            calls.append(asset)
            if asset.found:
                context["asset"] = _asset_facts(asset.result)
            else:
                context["notes"].append("The asset could not be looked up.")
        else:
            # The normal case: a reporter is not expected to know the asset tag.
            context["notes"].append("No specific asset was identified for this report.")

        return context, calls

    async def _call(self, tool_name: str, workflow_id: int, entity_id: int) -> ToolCallOutcome:
        return await self._tools.call(
            tool_name,
            workflow_id=workflow_id,
            entity_id=entity_id,
            agent_name=self.name,
            allowed_tools=self.ALLOWED_TOOLS,
        )

    @staticmethod
    def _render_data(description: str, context: dict[str, Any]) -> str:
        """
        The report and its context as ONE JSON object — the structural half of the
        prompt-injection defence. See the module docstring and DiagnosticAgent._render_data.
        """
        payload = {"report": {"description": description}, **context}
        return json.dumps(payload, indent=2, ensure_ascii=False)


# ----------------------------------------------------------------------
# Trimming tool results to what tells the model what is already known.
#
# Where the fault is and what the equipment is — nothing about its history, which is not
# this agent's to read. Every read is a .get() on a checked dict, because run() must not
# raise on an oddly shaped reply.
# ----------------------------------------------------------------------


def _room_facts(result: Any) -> dict[str, Any] | None:
    if not isinstance(result, dict):
        return None
    return {"name": result.get("name"), "code": result.get("code"), "floor": result.get("floor")}


def _asset_facts(result: Any) -> dict[str, Any] | None:
    if not isinstance(result, dict):
        return None

    asset = result.get("asset") if isinstance(result.get("asset"), dict) else {}
    return {
        "asset_tag": asset.get("assetTag"),
        "name": asset.get("name"),
        "category": result.get("categoryName"),
        "room": result.get("roomName"),
        "manufacturer": asset.get("manufacturer"),
        "model": asset.get("model"),
    }
