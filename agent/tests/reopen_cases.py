"""
The reopen golden case — a projector diagnosed, repaired as a temporary fix, and reported
again — as the tool replies the diagnostic sees on its FIRST run and on its SECOND. Shared by
tests/test_reopen.py (what the CODE does with it) and evals/test_reopen_live.py (what the
MODEL does with it), the same split as verification_cases.py, so both halves are about
exactly the same data.

Every tool reply is in the shape the API's tool router returns (camelCase, the DTOs named
beside each). Not a test file: pytest does not collect it.

## Why not the seeded projector

PRJ-MAB101-01's history already points at the cooling fan, and its first diagnosis says so
(see evals/test_diagnostic_live.py). Reopened, it would be diagnosed with the same cause more
confidently — a second opinion that agrees with the first cannot show the second one read
anything new. This asset's first-run history points somewhere else on purpose.

## The story

  first run    The picture drops out mid lecture. The only earlier visit for that symptom
               found a split HDMI cable and replaced it; the other is an annual service.
               Read honestly, the evidence points at the cabling again.
  the repair   Work order 51. The technician could not reproduce a signal fault, found the
               unit very hot at the exhaust with a rattling fan, cleaned the vents and filter
               and wrote it up as a TEMPORARY FIX — the ServiceRecord completion appends.
  since        A new report, filed after that repair: it cut out again, it was rattling, the
               vent air was hot.
  second run   The same three tools, asked again. The history now leads with the temporary
               fix and the open reports include the new one. The evidence has moved from the
               cable to the cooling fan.
"""

from __future__ import annotations

from typing import Any

from schemas import RunRequest, ToolCallOutcome

ASSET_ID = 9
FAILED_WORK_ORDER_ID = 51

DESCRIPTION = (
    "Seminar Room 204 projector keeps cutting out during lectures - the picture goes black "
    "and comes back after a minute or so."
)

# The note the technician wrote when completing work order 51, VERBATIM as the service
# record carries it. Terse and unpolished, like every seeded note: the agent's job is reading
# technicians' text, and clean prose here would flatter it.
TEMPORARY_FIX_NOTE = (
    "no signal fault not reproduced, hdmi + cable ok. unit v hot at exhaust, fan rattling on "
    "startup. cleaned intake filter + vents, ran 30min ok. temporary fix - fan needs replacing."
)

NEW_REPORT_DESCRIPTION = (
    "Projector in 204 cut out again 20 mins into the 10am lecture. It was rattling loudly and "
    "the air coming out of the side vent was really hot."
)


def request(*, reopened: bool) -> RunRequest:
    """What the API sends: the original report, the asset, and — second time — reopened."""
    return RunRequest(
        workflow_id=70,
        description=DESCRIPTION,
        room_id=12,
        asset_id=ASSET_ID,
        reopened=reopened,
    )


class ChangingTools:
    """
    A tool client whose answers are the database as it stands NOW: set `results` between
    runs and the next call sees the change, exactly as a row appended by a completion would
    be seen. Records every call, so a test can tell a fresh lookup from a remembered one.
    """

    def __init__(self, results: dict[str, Any]) -> None:
        self.results = results
        self.asked: list[tuple[str, int]] = []

    async def call(self, tool_name, *, workflow_id, entity_id, agent_name, allowed_tools):
        assert tool_name in allowed_tools, f"the agent asked for {tool_name}, outside its subset"
        self.asked.append((tool_name, entity_id))
        return ToolCallOutcome(tool=tool_name, found=True, result=self.results[tool_name])


def _visit(record_id: int, on: str, note: str, outcome: str, work_order_id: int | None) -> dict:
    """ServiceRecordDto, as get_asset_service_history returns it."""
    return {
        "id": record_id,
        "assetId": ASSET_ID,
        "servicedOn": on,
        "technicianName": "N. Jayasinghe",
        "technicianNote": note,
        "outcome": outcome,
        "workOrderId": work_order_id,
    }


def _report(report_id: int, created_at: str, description: str, status: str) -> dict:
    """ReportDto, as get_related_open_reports returns it."""
    return {
        "id": report_id,
        "reporterId": 7,
        "roomId": 12,
        "assetId": ASSET_ID,
        "description": description,
        "status": status,
        "photoUrl": None,
        "createdAt": created_at,
        "updatedAt": created_at,
    }


ASSET = {
    "asset": {
        "id": ASSET_ID,
        "assetTag": "PRJ-ENG204-01",
        "name": "Seminar Room 204 Projector",
        "assetCategoryId": 1,
        "roomId": 12,
        "manufacturer": "Epson",
        "model": "EB-L200F",
        "installedOn": "2024-02-05",
        "warrantyExpiresOn": "2026-02-05",
        "status": "Active",
    },
    "categoryName": "Projector",
    "roomName": "Seminar Room 204",
}

EARLIER_VISITS = [
    _visit(
        31,
        "2026-06-18",
        "picture dropping out mid lecture. hdmi cable split at lectern end, replaced cable. "
        "tested 20min ok.",
        "Resolved",
        None,
    ),
    _visit(22, "2026-02-10", "annual service. filter cleaned, lamp 1310 hrs, all ok.", "Resolved", None),
]

# The report the workflow was raised for. Still open on both runs — WorkOrderRaised, never
# Closed — so it appears in other_open_reports both times, and is not a second report.
ORIGINAL_REPORT = _report(60, "2026-08-28T04:10:00Z", DESCRIPTION, "Submitted")

# What the diagnostic reads BEFORE the repair.
FIRST_RUN: dict[str, Any] = {
    "get_asset": ASSET,
    "get_asset_service_history": EARLIER_VISITS,
    "get_related_open_reports": [ORIGINAL_REPORT],
}

# The record completion appended (ServiceRecord.WorkOrderId set), and the report filed after it.
REPAIR_RECORD = _visit(40, "2026-09-05", TEMPORARY_FIX_NOTE, "TemporaryFix", FAILED_WORK_ORDER_ID)
NEW_REPORT = _report(61, "2026-09-12T04:55:00Z", NEW_REPORT_DESCRIPTION, "Submitted")

# What the diagnostic reads AFTER verification reopened it: newest first, as the API orders
# both lists, so the repair leads the history and the new report leads the open reports.
SECOND_RUN: dict[str, Any] = {
    "get_asset": ASSET,
    "get_asset_service_history": [REPAIR_RECORD, *EARLIER_VISITS],
    "get_related_open_reports": [
        NEW_REPORT,
        {**ORIGINAL_REPORT, "status": "WorkOrderRaised"},
    ],
}
