"""
VerificationAgent — judges whether a completed repair actually held.

Owner: (assign one team member here)

## One round, on purpose

Same constraint as every other agent. It runs exactly once per verification: it receives
the repair being verified and the reporter's answer, looks up the work order, the asset's
service history and the reports filed on it since, returns ONE verdict, and is finished.
No conversation history, no follow-up, no message field.

## It weighs. C# decides.

The check's Status is set in C# from the reporter's yes or no, in the same SaveChanges as
the answer. This agent's `outcome` is stored beside it as VerificationCheck.AgentOutcome —
a string, read by a human, acted on by nothing. It weighs three things the reporter's
toggle cannot: what the technician wrote, whether the fault has been reported again, and
whether this asset keeps coming back.

## The facts are code's, the judgement is the model's

"Escalate, this keeps happening" is the model's call. HOW MANY times it has happened is
not: the count, the days since completion and which reports came in after the repair are
all worked out here, in plain Python, from what the tools returned — never left for the
model to count or date-compare. See VerificationInput.

## Everything it reads is data

The reporter's comment is typed by a member of the public, the notes by technicians, the
new reports by whoever filed them. All of it goes into the prompt as ONE JSON object
between markers, never spliced in as raw text — the same defence, and the same reasoning,
as DiagnosticAgent._render_data.
"""

from __future__ import annotations

import json
import logging
from datetime import date, datetime, timezone
from typing import Any, Callable

from pydantic import ValidationError

from llm_client import LlmClient
from prompts import load_prompt, render_prompt
from schemas import (
    MAX_TOOL_HISTORY_ROWS,
    MAX_VERIFICATION_REASON,
    AgentStatus,
    RunRequest,
    ToolCallOutcome,
    VerificationInput,
    VerificationOutput,
    VerificationResult,
)
from tools import ToolClient

logger = logging.getLogger(__name__)


def _utc_today() -> date:
    return datetime.now(timezone.utc).date()


class VerificationAgent:
    """Judges one completed repair: confirm, reopen or escalate."""

    name = "verification"

    # THIS AGENT'S TOOL SUBSET, and a different one again — required, not stylistic.
    # get_work_order is its alone: it is the only agent asked about one specific repair.
    # It shares get_asset_service_history with the diagnostic and the strategist, because
    # "has this failed before" is read from the same history. It has
    # get_related_open_reports, which the strategist does not, because "has anyone
    # reported it again" is half the question. It has NO get_asset: the work order names
    # the asset, and nothing about whether a repair held turns on its warranty or model.
    #
    # A tuple, so it cannot be appended to at runtime. The API's hardcoded allow-list in
    # InternalToolsController still has the final say.
    ALLOWED_TOOLS: tuple[str, ...] = (
        "get_asset_service_history",
        "get_related_open_reports",
        "get_work_order",
    )

    SYSTEM_PROMPT = "verification.md"
    USER_PROMPT = "verification_user.md"

    def __init__(
        self,
        llm: LlmClient,
        tools: ToolClient,
        today: Callable[[], date] = _utc_today,
    ) -> None:
        self._llm = llm
        self._tools = tools
        # Injected so a test can stand on either side of a date without the real clock —
        # the Python counterpart of the API's TimeProvider.
        self._today = today

    async def run(self, request: RunRequest) -> VerificationResult:
        """
        Returns a well-formed result in every case. No verification in the request, a
        repair that cannot be looked up, a provider outage or a model that cannot produce
        valid JSON all yield status safe_failure with no output, never an exception.
        """
        if request.verification is None:
            # graph.py only routes here when there is one; run() still must not raise.
            return self._safe_failure("No verification was requested.", [])

        verification_input, tool_calls, error = await self._gather_input(request)

        if verification_input is None:
            logger.warning(
                "Verification fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                error,
            )
            return self._safe_failure(error, tool_calls)

        user_prompt = render_prompt(
            self.USER_PROMPT,
            data=self._render_data(verification_input),
            max_reason=MAX_VERIFICATION_REASON,
        )

        result = await self._llm.complete_json(
            system=load_prompt(self.SYSTEM_PROMPT),
            user=user_prompt,
            schema=VerificationOutput,
        )

        if not result.ok or result.data is None:
            logger.warning(
                "Verification fell back to safe failure for workflow %s: %s",
                request.workflow_id,
                result.error,
            )
            return self._safe_failure(result.error, tool_calls)

        output = result.data
        assert isinstance(output, VerificationOutput)

        return VerificationResult(
            agent=self.name,
            status=AgentStatus.ok,
            output=output,
            tool_calls=tool_calls,
        )

    def _safe_failure(self, error: str | None, tool_calls: list[ToolCallOutcome]) -> VerificationResult:
        # No output rather than a placeholder: no verdict is not a verdict to confirm. The
        # check keeps the status the reporter's answer gave it, and a human reads the rest.
        return VerificationResult(
            agent=self.name,
            status=AgentStatus.safe_failure,
            output=None,
            error=error,
            tool_calls=tool_calls,
        )

    async def _gather_input(
        self,
        request: RunRequest,
    ) -> tuple[VerificationInput | None, list[ToolCallOutcome], str | None]:
        """
        Looks up the repair, the asset's history and the reports filed since, and builds
        the VerificationInput. Returns (None, calls, reason) when there is nothing to judge.

        The work order is the one lookup it cannot do without: with no resolution note and
        no completion date there is no claim to test and no "since" to measure from. The
        other two degrade to a note, so the model can say what it could not see.
        """
        verification = request.verification
        assert verification is not None
        workflow_id = request.workflow_id
        calls: list[ToolCallOutcome] = []
        notes: list[str] = []

        order = await self._call("get_work_order", workflow_id, verification.work_order_id)
        calls.append(order)

        if not order.found or not isinstance(order.result, dict):
            return None, calls, (
                f"The repair being verified (work order {verification.work_order_id}) "
                "could not be looked up."
            )

        asset_id = order.result.get("assetId")
        if not isinstance(asset_id, int):
            return None, calls, (
                f"Work order {verification.work_order_id} names no asset, so there is no "
                "history to read."
            )

        completed_at = _parse_utc(order.result.get("completedAt"))
        days_since_completion: int | None = None
        if completed_at is None:
            notes.append(
                "The repair has no completion date on record, so the days since completion "
                "and the reports filed after it cannot be worked out."
            )
        else:
            # Clamped at zero: a clock a few seconds behind the API's is not a repair from
            # the future.
            days_since_completion = max(0, (self._today() - completed_at.date()).days)

        history = await self._call("get_asset_service_history", workflow_id, asset_id)
        calls.append(history)

        # Plain dicts until VerificationInput validates them below, inside the one try —
        # so a malformed row is a safe failure, never an exception out of run().
        visits: list[dict[str, Any]] | None = None
        if history.found and isinstance(history.result, list):
            visits = [_visit(r, verification.work_order_id) for r in history.result]
            if not visits:
                notes.append("This asset has no service history on record.")
            elif len(visits) >= MAX_TOOL_HISTORY_ROWS:
                notes.append(
                    f"The history is capped at {MAX_TOOL_HISTORY_ROWS} visits, so the count "
                    "is a minimum."
                )
        else:
            notes.append("The service history could not be retrieved.")

        reports = await self._call("get_related_open_reports", workflow_id, asset_id)
        calls.append(reports)

        new_reports: list[dict[str, Any]] | None = None
        if not (reports.found and isinstance(reports.result, list)):
            # Not the same as "nobody reported it again": the model must not read a failed
            # lookup as a clean week.
            notes.append("Reports on this asset could not be retrieved.")
        elif completed_at is not None:
            original_report_id = order.result.get("reportId")
            new_reports = [
                _new_report(r)
                for r in reports.result
                if isinstance(r, dict)
                and r.get("id") != original_report_id
                and _filed_after(r, completed_at)
            ]

        try:
            verification_input = VerificationInput(
                fault_reported=request.description,
                resolution_note=order.result.get("resolutionNote"),
                days_since_completion=days_since_completion,
                new_reports_since_completion=new_reports,
                reporter_confirmed=verification.reporter_confirmed,
                reporter_comment=verification.reporter_comment,
                service_history_newest_first=visits,
                # THE COUNT, by code, from the tool's own rows.
                service_visits_on_record=len(visits) if visits is not None else None,
                notes=notes,
            )
        except ValidationError as exc:
            # A tool reply outside the API's own column limits: the two contracts have
            # drifted. Refused rather than trimmed, and never raised.
            return None, calls, f"Tool data did not fit the verification input: {exc.errors()[0]['msg']}"

        return verification_input, calls, None

    async def _call(self, tool_name: str, workflow_id: int, entity_id: int) -> ToolCallOutcome:
        return await self._tools.call(
            tool_name,
            workflow_id=workflow_id,
            entity_id=entity_id,
            agent_name=self.name,
            allowed_tools=self.ALLOWED_TOOLS,
        )

    @staticmethod
    def _render_data(verification_input: VerificationInput) -> str:
        """
        The whole untrusted payload as ONE JSON object — the structural half of the
        prompt-injection defence, exactly as in DiagnosticAgent._render_data. json.dumps
        escapes every newline inside a string, so no text in the reporter's comment, a
        technician's note or a new report can put a closing marker on a line of its own.
        """
        return json.dumps(verification_input.model_dump(mode="json"), indent=2, ensure_ascii=False)


# ----------------------------------------------------------------------
# Reading tool results. Every read is a .get() on a checked dict, because run() must not
# raise on an oddly shaped reply.
# ----------------------------------------------------------------------


def _parse_utc(value: Any) -> datetime | None:
    """
    An API timestamp as an aware UTC datetime. The API writes UTC with a Z from
    PostgreSQL and with no zone at all from SQLite; both mean UTC, so a naive value is
    read as UTC rather than as this machine's local time.
    """
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.strip())
    except ValueError:
        return None
    return parsed.replace(tzinfo=timezone.utc) if parsed.tzinfo is None else parsed.astimezone(timezone.utc)


def _filed_after(report: dict[str, Any], completed_at: datetime) -> bool:
    """
    Whether a report came in after the repair finished. A report with no readable date is
    left out rather than guessed at: counting it would claim the fault came back on the
    strength of a timestamp nobody could read.
    """
    filed = _parse_utc(report.get("createdAt"))
    return filed is not None and filed >= completed_at


def _visit(record: Any, work_order_id: int) -> dict[str, Any]:
    """A history row as ServiceVisit's fields; validated when VerificationInput is built."""
    if not isinstance(record, dict):
        return {}

    return {
        "serviced_on": record.get("servicedOn"),
        "outcome": record.get("outcome"),
        "technician_note": record.get("technicianNote"),
        "is_this_repair": record.get("workOrderId") == work_order_id,
    }


def _new_report(report: dict[str, Any]) -> dict[str, Any]:
    """A report as NewReport's fields; validated when VerificationInput is built."""
    return {
        "reported_at": report.get("createdAt"),
        "status": report.get("status"),
        "description": report.get("description") or "(no description)",
    }
