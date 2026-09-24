"""
ResolutionStrategist — proposes how a diagnosed fault should be handled, and what it will cost.

Owner: (assign one team member here)

## One round, on purpose

Same constraint as the clarifier and the diagnostic. This agent runs exactly once per
/run: it receives the report, the diagnosis and — on a re-run — the manager's revision
note, looks up the asset and the work already open around it, returns ONE proposal, and is
finished. No conversation history, no follow-up, no message field.

## It proposes. C# decides.

StrategistOutput has no approval field, and never will. The API compares
`estimated_cost` against Approval:CostThreshold in C#, and sends every
escalate_replacement to a manager whatever it costs — the agent is not even told what the
threshold is, so it has nothing to aim an estimate at. A proposal to consolidate is
checked here against the open orders the agent was actually shown, and has to be checked
again by the API when it raises anything: an id is a claim until C# has looked it up.

## Everything it reads is data

The report is typed by a member of the public, the history by technicians, the diagnosis
by another model and the revision note by a manager. All of it goes into the prompt as ONE
JSON object between markers, never spliced in as raw text — the same defence, and the same
reasoning, as the diagnostic. See DiagnosticAgent._render_data.
"""

from __future__ import annotations

import json
import logging
from typing import Any

from llm_client import LlmClient
from prompts import load_prompt, render_prompt
from schemas import (
    MAX_JUSTIFICATION,
    AgentStatus,
    DiagnosticResult,
    RunRequest,
    StrategistInput,
    StrategistOutput,
    StrategistResult,
    ToolCallOutcome,
)
from tools import ToolClient

logger = logging.getLogger(__name__)


class ResolutionStrategist:
    """Proposes one strategy, a cost estimate and an urgency for a diagnosed fault."""

    name = "strategist"

    # THIS AGENT'S TOOL SUBSET, declared explicitly and locally — and different from both
    # other agents'. It shares get_asset and get_asset_service_history with the diagnostic,
    # because a strategy has to be argued from the same machine's history; it does NOT have
    # get_related_open_reports, because by now the fault is diagnosed and the question is
    # the work, not the complaints. get_open_work_orders is its alone: the only agent that
    # can propose consolidating jobs is the only one that can see the jobs.
    #
    # A tuple, so it cannot be appended to at runtime. The API's hardcoded allow-list in
    # InternalToolsController still has the final say.
    ALLOWED_TOOLS: tuple[str, ...] = (
        "get_asset",
        "get_asset_service_history",
        "get_open_work_orders",
    )

    SYSTEM_PROMPT = "strategist.md"
    USER_PROMPT = "strategist_user.md"

    def __init__(self, llm: LlmClient, tools: ToolClient) -> None:
        self._llm = llm
        self._tools = tools

    async def run(
        self,
        request: RunRequest,
        diagnosis: DiagnosticResult | None,
    ) -> StrategistResult:
        """
        Returns a well-formed result in every case. A provider outage, a model that cannot
        produce valid JSON, or a proposal naming work orders it was never shown yields
        status safe_failure with no output, never an exception.
        """
        # Only what this agent may see; the prompt is rendered from this, not the request.
        strategist_input = StrategistInput.from_run(request, diagnosis)

        context, tool_calls = await self._gather_context(request.workflow_id, strategist_input)

        user_prompt = render_prompt(
            self.USER_PROMPT,
            data=self._render_data(strategist_input, context),
            max_justification=MAX_JUSTIFICATION,
        )

        result = await self._llm.complete_json(
            system=load_prompt(self.SYSTEM_PROMPT),
            user=user_prompt,
            schema=StrategistOutput,
        )

        if not result.ok or result.data is None:
            logger.warning(
                "Strategist fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                result.error,
            )
            return self._safe_failure(result.error, tool_calls)

        output = result.data
        assert isinstance(output, StrategistOutput)

        # Consolidating with a work order the agent was never shown is a proposal about
        # an order that may not exist, or one in another room. The schema cannot know what
        # was shown, so this is checked here — as a refusal, not a repair: dropping the
        # unknown ids would quietly turn the proposal into a different one.
        shown = {order["id"] for order in context["open_work_orders"] if order.get("id") is not None}
        unknown = [i for i in output.consolidate_with_work_order_ids if i not in shown]
        if unknown:
            error = f"Proposed consolidating with work orders it was not shown: {unknown}."
            logger.warning("Strategist output rejected for workflow %s: %s", request.workflow_id, error)
            return self._safe_failure(error, tool_calls)

        return StrategistResult(
            agent=self.name,
            status=AgentStatus.ok,
            output=output,
            tool_calls=tool_calls,
        )

    def _safe_failure(self, error: str | None, tool_calls: list[ToolCallOutcome]) -> StrategistResult:
        # No output rather than a placeholder: no proposal is not a proposal to defer, and
        # a made-up strategy would be a decision nobody made. A manager decides instead.
        return StrategistResult(
            agent=self.name,
            status=AgentStatus.safe_failure,
            output=None,
            error=error,
            tool_calls=tool_calls,
        )

    async def _gather_context(
        self,
        workflow_id: int,
        strategist_input: StrategistInput,
    ) -> tuple[dict[str, Any], list[ToolCallOutcome]]:
        """
        Looks up the asset, its service history and the work orders open in its room.

        Missing context is not an error — the agent has less to go on, and `notes` tells
        the model why, so it can say so rather than filling the gap with a guess.
        """
        context: dict[str, Any] = {
            "asset": None,
            "service_history_newest_first": [],
            "open_work_orders": [],
            "notes": [],
        }
        calls: list[ToolCallOutcome] = []

        if strategist_input.diagnosis is None:
            context["notes"].append(
                "No diagnosis was produced for this report, so the cause is not established."
            )

        asset_id = strategist_input.asset_id
        if asset_id is None:
            context["notes"].append(
                "No asset was identified for this report, so there is no service history "
                "and no open work to consider."
            )
            return context, calls

        asset = await self._call("get_asset", workflow_id, asset_id)
        calls.append(asset)

        if not asset.found:
            # Stop rather than ask two more questions about a machine that is not there.
            context["notes"].append(
                f"Asset {asset_id} could not be looked up, so there is no service history "
                "and no open work to consider."
            )
            return context, calls

        context["asset"] = _asset_facts(asset.result)

        history = await self._call("get_asset_service_history", workflow_id, asset_id)
        calls.append(history)

        if history.found and isinstance(history.result, list):
            context["service_history_newest_first"] = [_record_facts(r) for r in history.result]
            if not history.result:
                context["notes"].append("This asset has no service history on record.")
        else:
            context["notes"].append("The service history could not be retrieved.")

        orders = await self._call("get_open_work_orders", workflow_id, asset_id)
        calls.append(orders)

        if orders.found and isinstance(orders.result, list):
            context["open_work_orders"] = [_order_facts(o) for o in orders.result]
        else:
            # Not the same as "nothing open": the model must not propose consolidation on
            # the strength of a lookup that failed.
            context["notes"].append(
                "Open work orders could not be retrieved, so consolidation cannot be considered."
            )

        return context, calls

    async def _call(self, tool_name: str, workflow_id: int, asset_id: int) -> ToolCallOutcome:
        return await self._tools.call(
            tool_name,
            workflow_id=workflow_id,
            entity_id=asset_id,
            agent_name=self.name,
            allowed_tools=self.ALLOWED_TOOLS,
        )

    @staticmethod
    def _render_data(strategist_input: StrategistInput, context: dict[str, Any]) -> str:
        """
        The whole untrusted payload as ONE JSON object — the structural half of the
        prompt-injection defence, exactly as in DiagnosticAgent._render_data. json.dumps
        escapes every newline inside a string, so no text in the report, the diagnosis or
        the revision note can put a closing marker on a line of its own.
        """
        diagnosis = strategist_input.diagnosis
        payload = {
            "report": {"description": strategist_input.description},
            "diagnosis": diagnosis.model_dump(mode="json") if diagnosis is not None else None,
            "manager_revision_note": strategist_input.revision_note,
            **context,
        }
        return json.dumps(payload, indent=2, ensure_ascii=False)


# ----------------------------------------------------------------------
# Trimming tool results to the fields a strategy is argued from. Every read is a .get()
# on a checked dict, because run() must not raise on an oddly shaped reply.
# ----------------------------------------------------------------------


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
        "installed_on": asset.get("installedOn"),
        "warranty_expires_on": asset.get("warrantyExpiresOn"),
        "status": asset.get("status"),
    }


def _record_facts(record: Any) -> dict[str, Any]:
    if not isinstance(record, dict):
        return {}

    return {
        "serviced_on": record.get("servicedOn"),
        "outcome": record.get("outcome"),
        "technician_note": record.get("technicianNote"),
    }


def _order_facts(order: Any) -> dict[str, Any]:
    """
    An open work order as the strategist needs it: which order, on which machine, how far
    along, and what is already planned. Not the technician's name — nothing about a
    strategy turns on who is going.
    """
    if not isinstance(order, dict):
        return {}

    return {
        "id": order.get("id"),
        "asset_id": order.get("assetId"),
        "asset_tag": order.get("assetTag"),
        "status": order.get("status"),
        "strategy": order.get("strategy"),
        "estimated_cost": order.get("estimatedCost"),
        "raised_at": order.get("createdAt"),
    }
