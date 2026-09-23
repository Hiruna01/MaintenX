"""
HTTP client for the API's tool router — this service's only route to campus data.

There are no database credentials in this process. Everything an agent can read, it reads
by calling POST {API_BASE_URL}/api/internal/tools/{toolName} with the shared secret in the
X-Agent-Secret header.

## Where the security boundary actually is

`allowed_tools` below is checked before a request goes out, and that check is a
CONVENIENCE, not the security boundary. The real allow-list is a hardcoded C# dictionary
in the API's InternalToolsController: a name that is not a key there cannot reach any
service, whatever this file does. If someone deleted the check below, the worst outcome
would be a 404 and a warning line in the API's log.

That ordering is deliberate. The enforcement lives on the far side of the network from
anything the model can influence.

## Request and response shape

The API is ASP.NET Core, so JSON is camelCase on the wire:

    POST /api/internal/tools/get_room
    X-Agent-Secret: <shared secret>
    {"workflowId": 12, "id": 3, "agentName": "clarifier"}

    200 {"tool": "get_room", "found": true, "result": {...}}
    401 missing or wrong secret
    404 tool name not in the API's allow-list
    400 workflow id does not exist
"""

from __future__ import annotations

import logging
from typing import Any, Collection

import httpx

from config import Settings, get_settings
from schemas import ToolCallOutcome

logger = logging.getLogger(__name__)

# Header name must match AgentSettings.SecretHeaderName on the API side.
SECRET_HEADER = "X-Agent-Secret"

# The seeded projector PRJ-MAB101-01 and its planted repeat-failure history, copied
# VERBATIM from api/Data/DbSeeder.cs (the three PRJ-MAB101-01 service records), in the
# shape the API's asset tools return. Newest first, as get_asset_service_history orders it.
#
# Used as the STUB_MODE reply for the asset tools, and by the diagnostic's golden-case
# test and eval. If the seed changes, change this with it — the notes are the evidence,
# and the golden case is only as honest as this copy.
SEEDED_PROJECTOR_RESULTS: dict[str, Any] = {
    "get_asset": {
        "asset": {
            "id": 1,
            "assetTag": "PRJ-MAB101-01",
            "name": "Lecture Hall A Projector",
            "assetCategoryId": 1,
            "roomId": 1,
            "manufacturer": "Epson",
            "model": "EB-990U",
            "installedOn": "2023-08-14",
            "warrantyExpiresOn": "2025-08-14",
            "status": "Active",
        },
        "categoryName": "Projector",
        "roomName": "Lecture Hall A",
    },
    "get_asset_service_history": [
        {
            "id": 3,
            "assetId": 1,
            "servicedOn": "2026-09-02",
            "technicianName": "S. Fernando",
            "technicianNote": (
                "cleaned filter, unit still running hot, temporary fix, fan bearing sounds "
                "weak - recommend replacement before next term"
            ),
            "outcome": "TemporaryFix",
        },
        {
            "id": 2,
            "assetId": 1,
            "servicedOn": "2026-07-03",
            "technicianName": "K. Perera",
            "technicianNote": (
                "same complaint as May. air filter choked w/ dust, lamp hrs high. cleaned "
                "filter, ok on test after 30min."
            ),
            "outcome": "TemporaryFix",
        },
        {
            "id": 1,
            "assetId": 1,
            "servicedOn": "2026-05-12",
            "technicianName": "K. Perera",
            "technicianNote": (
                "projector cutting out mid lecture. checked hdmi + cable, reseated both. ran "
                "20min on test, no fault seen. adv. dept to report again if recurs."
            ),
            "outcome": "NoFaultFound",
        },
    ],
    "get_related_open_reports": [],
}

# Fixed context used in STUB_MODE so tests never need a running API.
_STUB_RESULTS: dict[str, Any] = {
    **SEEDED_PROJECTOR_RESULTS,
    "get_room": {
        "id": 1,
        "buildingId": 1,
        "name": "Lecture Hall A",
        "code": "A-101",
        "floor": 1,
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z",
    },
    "get_building": {
        "id": 1,
        "name": "Engineering Block",
        "code": "ENG",
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z",
    },
}


class ToolClient:
    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()

    async def call(
        self,
        tool_name: str,
        *,
        workflow_id: int,
        entity_id: int,
        agent_name: str,
        allowed_tools: Collection[str],
    ) -> ToolCallOutcome:
        """
        Invokes one tool. Never raises — a failure comes back as an outcome with `error`
        set, because an agent losing its context is a degraded answer, not a crash.
        """
        if tool_name not in allowed_tools:
            # The agent asked for something outside its own declared subset. This is a
            # bug in the agent, so it is loud, and the call never leaves the process.
            logger.error(
                "Agent %s tried to call %r, which is not in its allowed tool list %s.",
                agent_name,
                tool_name,
                sorted(allowed_tools),
            )
            return ToolCallOutcome(
                tool=tool_name,
                found=False,
                error=f"Tool {tool_name!r} is not allowed for agent {agent_name!r}.",
            )

        if self._settings.stub_mode:
            result = _STUB_RESULTS.get(tool_name)
            return ToolCallOutcome(tool=tool_name, found=result is not None, result=result)

        url = f"{self._settings.api_base_url.rstrip('/')}/api/internal/tools/{tool_name}"
        payload = {"workflowId": workflow_id, "id": entity_id, "agentName": agent_name}
        headers = {SECRET_HEADER: self._settings.agent_shared_secret}

        try:
            async with httpx.AsyncClient(timeout=self._settings.tool_timeout_seconds) as client:
                response = await client.post(url, json=payload, headers=headers)
        except httpx.HTTPError as exc:
            logger.warning("Tool call %s failed to reach the API: %s", tool_name, exc)
            return ToolCallOutcome(tool=tool_name, found=False, error=f"Tool call failed: {exc}")

        if response.status_code == httpx.codes.NOT_FOUND:
            # The API's allow-list rejected the name. Worth surfacing plainly: it means
            # this file and the C# dictionary have drifted apart.
            logger.error("The API rejected tool %r as unknown.", tool_name)
            return ToolCallOutcome(
                tool=tool_name, found=False, error=f"The API does not expose a tool named {tool_name!r}."
            )

        if response.status_code == httpx.codes.UNAUTHORIZED:
            logger.error("The API rejected the agent shared secret. Check AGENT_SHARED_SECRET.")
            return ToolCallOutcome(
                tool=tool_name, found=False, error="The API rejected the agent shared secret."
            )

        if response.status_code != httpx.codes.OK:
            logger.warning("Tool %s returned HTTP %s.", tool_name, response.status_code)
            return ToolCallOutcome(
                tool=tool_name, found=False, error=f"Tool returned HTTP {response.status_code}."
            )

        try:
            body = response.json()
        except ValueError as exc:
            return ToolCallOutcome(tool=tool_name, found=False, error=f"Tool returned non-JSON: {exc}")

        return ToolCallOutcome(
            tool=tool_name,
            found=bool(body.get("found")),
            result=body.get("result"),
        )
