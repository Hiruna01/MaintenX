"""
FastAPI app for the agent service.

Two endpoints, both called by the ASP.NET Core API and by nothing else — React and
Flutter never talk to this service.

    GET  /health   liveness plus which mode the process is in
    POST /run      run the workflow graph over one report, or one completed repair

/run requires the X-Agent-Secret header — the same shared secret the agent's own tool calls
send back to the API — so only the API can start an agent run, not anything else that can
reach this port. It fails closed: with no secret configured, every call is refused.

/run always answers with a well-formed RunResponse. An agent that cannot do its job
returns status "safe_failure" with empty output and a 200, because the caller is a
background worker recording a workflow step, not a user waiting on an error page.
"""

from __future__ import annotations

import hmac
import logging
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, status

from agents.clarifier import ClarifierAgent
from agents.diagnostic import DiagnosticAgent
from agents.planner import PlannerAgent
from agents.strategist import ResolutionStrategist
from agents.verification import VerificationAgent
from config import get_settings
from graph import build_graph
from llm_client import LlmClient
from schemas import ClarifierOutput, RunRequest, RunResponse
from tools import ToolClient

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Builds the graph once at startup rather than per request."""
    settings = get_settings()

    if settings.stub_mode:
        logger.warning("STUB_MODE is on: no LLM or API calls will be made.")

    if not settings.agent_shared_secret:
        logger.warning(
            "AGENT_SHARED_SECRET is empty; every /run call and every tool call will be refused with 401."
        )

    llm = LlmClient(settings)
    tools = ToolClient(settings)

    app.state.settings = settings
    app.state.graph = build_graph(
        ClarifierAgent(llm=llm, tools=tools),
        DiagnosticAgent(llm=llm, tools=tools),
        ResolutionStrategist(llm=llm, tools=tools),
        VerificationAgent(llm=llm, tools=tools),
        PlannerAgent(llm=llm, tools=tools),
    )

    yield


app = FastAPI(
    title="MaintenX Agent Service",
    description="Agent orchestration for campus maintenance. Holds no database credentials.",
    version="0.1.0",
    lifespan=lifespan,
)


@app.get("/health")
async def health() -> dict[str, object]:
    """
    Liveness check. Reports the model name but never the API key or the shared secret —
    a health endpoint is the classic place to leak a credential by accident.
    """
    settings = get_settings()
    return {
        "status": "healthy",
        "service": "agent",
        "stub_mode": settings.stub_mode,
        "model": settings.llm_model or None,
        "api_base_url": settings.api_base_url,
    }


def require_agent_secret(x_agent_secret: str | None = Header(default=None)) -> None:
    """
    Refuses any caller that does not send the shared secret. A dependency, so it runs BEFORE
    the body is validated: a caller without the secret gets a 401 and never a 422 describing
    what the body should have looked like — the same order as the API's AgentSecretFilter.
    Compared in constant time, and closed when no secret is configured.
    """
    expected = get_settings().agent_shared_secret
    if not expected or not x_agent_secret or not hmac.compare_digest(
        expected.encode("utf-8"), x_agent_secret.encode("utf-8")
    ):
        # Never the header's value in the log — only that it was wrong.
        logger.warning("Refused a /run call with a missing or incorrect agent secret.")
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Unauthorized")


@app.post("/run", response_model=RunResponse, dependencies=[Depends(require_agent_secret)])
async def run(request: RunRequest) -> RunResponse:
    """
    Runs the graph for one report and returns every agent's output.

    The clarifier's result stays at the top level, exactly where the API already reads
    it; the diagnosis and the strategist's proposal are attached beside it. Assembling the
    response is this layer's job, which keeps every node in graph.py a one-liner.

    A VERIFICATION run fills `verification` and nothing else. The clarifier did not run,
    so the top-level fields describe the run as a whole — the verification agent's name,
    status and error — with an empty question list, because nobody was asked anything.

    A RESUMED run — one carrying the reporter's clarification answers, or a repair that
    verification reopened — starts at the diagnostic, so again the clarifier did not run and the top-level fields are the first
    agent that did: the diagnostic's name, status and error, with an empty question list.

    A run the clarifier PAUSED carries its questions and no diagnosis or proposal: graph.py
    stopped there, and the API waits for the reporter.

    A FRESH run carries the planner's plan in `plan`, whichever way it went. When the plan
    left the clarifier out, the top-level fields are the diagnostic's, exactly as on a resumed
    run — which is how the API knows the clarifier did not run.
    """
    final_state = await app.state.graph.ainvoke(
        {
            "request": request,
            "plan": None,
            "response": None,
            "diagnosis": None,
            "strategy": None,
            "verification": None,
        }
    )

    if request.verification is not None:
        verdict = final_state["verification"]
        return RunResponse(
            workflow_id=request.workflow_id,
            agent=verdict.agent,
            status=verdict.status,
            output=ClarifierOutput(),
            error=verdict.error,
            attempts=verdict.attempts,
            duration_ms=verdict.duration_ms,
            verification=verdict,
        )

    if final_state["response"] is None:
        diagnosis = final_state["diagnosis"]
        return RunResponse(
            workflow_id=request.workflow_id,
            agent=diagnosis.agent,
            status=diagnosis.status,
            output=ClarifierOutput(),
            error=diagnosis.error,
            attempts=diagnosis.attempts,
            duration_ms=diagnosis.duration_ms,
            plan=final_state.get("plan"),
            diagnosis=diagnosis,
            strategy=final_state["strategy"],
        )

    return final_state["response"].model_copy(
        update={
            "plan": final_state.get("plan"),
            "diagnosis": final_state["diagnosis"],
            "strategy": final_state["strategy"],
        }
    )
