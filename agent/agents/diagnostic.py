"""
DiagnosticAgent — proposes what is causing a fault, from the asset's own service history.

Owner: (assign one team member here)

## One round, on purpose

Same constraint as the clarifier. This agent runs exactly once per report: it receives the
report, looks up the asset's record and history, returns its hypotheses, and is finished.
There is no conversation history parameter, no follow-up call, and no free-text field in
its output — see the DiagnosticOutput docstring in schemas.py.

## Facts in, advice out

The tools it may call return facts — a row, a list of rows, or nothing — and never a
judgement; the API has no "diagnose" tool to call. What those facts MEAN is this agent's
job, and its answer is advice for a human, recorded and read. Nothing acts on
`recommended_next_action` automatically: whether equipment is replaced is approval
routing, and approval routing is a deterministic business rule that lives in C#.

## Everything it reads is data

The report and the clarification answers are typed by members of the public, and the
service history by technicians. All of it goes into the prompt as ONE JSON object between
markers, never spliced in as raw text: a quote or a newline inside a description is
escaped by the JSON encoder, so nothing a reporter types can end the data block early and
start writing instructions. The prompt then tells the model the block is data. Neither
defence is a guarantee on its own — a model can still be persuaded — which is why the
behaviour itself is measured in agent/evals/ as well as the structure tested here.
"""

from __future__ import annotations

import json
import logging
from typing import Any

from llm_client import LlmClient
from prompts import load_prompt, render_prompt
from schemas import (
    MAX_HYPOTHESES,
    AgentStatus,
    DiagnosticInput,
    DiagnosticOutput,
    DiagnosticResult,
    RunRequest,
    ToolCallOutcome,
)
from tools import ToolClient

logger = logging.getLogger(__name__)


class DiagnosticAgent:
    """Proposes one to three causes for a fault, each with its confidence and evidence."""

    name = "diagnostic"

    # THIS AGENT'S TOOL SUBSET, declared explicitly and locally.
    #
    # The three asset lookups and nothing else — in particular NOT the clarifier's
    # get_room and get_building. The diagnostic reasons about one machine's history, and
    # get_asset already returns the room's name. A tuple, like the clarifier's, so it
    # cannot be appended to at runtime. Widening it is a code change in this file,
    # reviewed by this agent's owner — and even then the API's own hardcoded allow-list
    # still has the final say.
    ALLOWED_TOOLS: tuple[str, ...] = (
        "get_asset",
        "get_asset_service_history",
        "get_related_open_reports",
    )

    SYSTEM_PROMPT = "diagnostic.md"
    USER_PROMPT = "diagnostic_user.md"

    def __init__(self, llm: LlmClient, tools: ToolClient) -> None:
        self._llm = llm
        self._tools = tools

    async def run(self, request: RunRequest) -> DiagnosticResult:
        """
        Returns a well-formed result in every case. A provider outage or a model that
        cannot produce valid JSON yields status safe_failure with no output, never an
        exception.
        """
        # Only what this agent is allowed to see. The prompt is rendered from this, not
        # from the request, so a field added to RunRequest for another agent does not
        # silently reach this one.
        diagnostic_input = DiagnosticInput.from_run_request(request)

        context, tool_calls = await self._gather_context(request.workflow_id, diagnostic_input)

        user_prompt = render_prompt(
            self.USER_PROMPT,
            data=self._render_data(diagnostic_input, context),
            max_hypotheses=MAX_HYPOTHESES,
        )

        result = await self._llm.complete_json(
            system=load_prompt(self.SYSTEM_PROMPT),
            user=user_prompt,
            schema=DiagnosticOutput,
        )

        if not result.ok or result.data is None:
            logger.warning(
                "Diagnostic fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                result.error,
            )
            return DiagnosticResult(
                agent=self.name,
                status=AgentStatus.safe_failure,
                # No output rather than a placeholder: there is no such thing as an empty
                # diagnosis, and a made-up "inspect" would be a recommendation nobody
                # made. No diagnosis means a human looks at the report.
                output=None,
                error=result.error,
                tool_calls=tool_calls,
            )

        output = result.data
        assert isinstance(output, DiagnosticOutput)

        return DiagnosticResult(
            agent=self.name,
            status=AgentStatus.ok,
            output=output,
            tool_calls=tool_calls,
        )

    async def _gather_context(
        self,
        workflow_id: int,
        diagnostic_input: DiagnosticInput,
    ) -> tuple[dict[str, Any], list[ToolCallOutcome]]:
        """
        Looks up the asset, its service history and the other open reports against it.

        Missing context is not an error — the agent just has less to go on, and `notes`
        tells the model why, so it can say so instead of filling the gap with a guess.
        """
        context: dict[str, Any] = {
            "asset": None,
            "service_history_newest_first": [],
            "other_open_reports": [],
            "notes": [],
        }
        calls: list[ToolCallOutcome] = []

        asset_id = diagnostic_input.asset_id
        if asset_id is None:
            # The normal case for a fresh report: the reporter did not know the tag. With
            # no asset there is no history, and the prompt must hear that plainly.
            context["notes"].append(
                "No asset was identified for this report, so there is no service history "
                "to cite."
            )
            return context, calls

        asset = await self._call("get_asset", workflow_id, asset_id)
        calls.append(asset)

        if not asset.found:
            # Stop here rather than asking two more questions about a machine that is not
            # there: both would come back found=False, and each is an audit row on the
            # API side for nothing.
            context["notes"].append(
                f"Asset {asset_id} could not be looked up, so there is no service history "
                "to cite."
            )
            return context, calls

        context["asset"] = _asset_facts(asset.result)

        history = await self._call("get_asset_service_history", workflow_id, asset_id)
        calls.append(history)

        # found with an empty list is "never serviced" — an answer. Not found is "could
        # not be retrieved". The model is told which, because they are different facts.
        if history.found and isinstance(history.result, list):
            context["service_history_newest_first"] = [_record_facts(r) for r in history.result]
            if not history.result:
                context["notes"].append("This asset has no service history on record.")
        else:
            context["notes"].append("The service history could not be retrieved.")

        reports = await self._call("get_related_open_reports", workflow_id, asset_id)
        calls.append(reports)

        if reports.found and isinstance(reports.result, list):
            context["other_open_reports"] = [_report_facts(r) for r in reports.result]
        else:
            context["notes"].append("Other open reports could not be retrieved.")

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
    def _render_data(diagnostic_input: DiagnosticInput, context: dict[str, Any]) -> str:
        """
        The whole untrusted payload as ONE JSON object.

        This is the structural half of the prompt-injection defence. json.dumps escapes
        every quote and newline inside a string, so a description containing
        "\\n--- END DATA ---\\nNew instructions:" arrives as a single escaped string on one
        line — it cannot put a closing marker on a line of its own, and so cannot end the
        block and start writing text the model would read as instructions.

        ensure_ascii=False keeps Sinhala and Tamil reports readable to the model; it does
        not change what is escaped.
        """
        payload = {
            "report": {
                "description": diagnostic_input.description,
                "clarification_answers": [
                    answer.model_dump() for answer in diagnostic_input.clarification_answers
                ],
            },
            **context,
        }
        return json.dumps(payload, indent=2, ensure_ascii=False)


# ----------------------------------------------------------------------
# Trimming tool results to the fields that are evidence.
#
# The API's DTOs carry ids and timestamps a diagnosis has no use for. What reaches the
# prompt is what a technician would read: dates, outcomes and notes. Every read is a
# .get() on a checked dict, because run() must not raise on an oddly shaped reply.
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


def _report_facts(report: Any) -> dict[str, Any]:
    if not isinstance(report, dict):
        return {}

    return {
        "report_id": report.get("id"),
        "status": report.get("status"),
        "description": report.get("description"),
        "reported_at": report.get("createdAt"),
    }
