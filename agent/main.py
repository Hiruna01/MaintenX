"""
FastAPI app for the agent service.

Two endpoints, both called by the ASP.NET Core API and by nothing else — React and
Flutter never talk to this service.

    GET  /health   liveness plus which mode the process is in
    POST /run      run the workflow graph over one report

/run always answers with a well-formed RunResponse. An agent that cannot do its job
returns status "safe_failure" with empty output and a 200, because the caller is a
background worker recording a workflow step, not a user waiting on an error page.
"""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI

from agents.clarifier import ClarifierAgent
from agents.diagnostic import DiagnosticAgent
from config import get_settings
from graph import build_graph
from llm_client import LlmClient
from schemas import RunRequest, RunResponse
from tools import ToolClient

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Builds the graph once at startup rather than per request."""
    settings = get_settings()

    if settings.stub_mode:
        logger.warning("STUB_MODE is on: no LLM or API calls will be made.")
    elif not settings.agent_shared_secret:
        logger.warning("AGENT_SHARED_SECRET is empty; every tool call will be rejected with 401.")

    llm = LlmClient(settings)
    tools = ToolClient(settings)

    app.state.settings = settings
    app.state.graph = build_graph(
        ClarifierAgent(llm=llm, tools=tools),
        DiagnosticAgent(llm=llm, tools=tools),
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


@app.post("/run", response_model=RunResponse)
async def run(request: RunRequest) -> RunResponse:
    """
    Runs the graph for one report and returns every agent's output.

    The clarifier's result stays at the top level, exactly where the API already reads
    it; the diagnosis is attached beside it. Assembling the response is this layer's
    job, which keeps every node in graph.py a one-liner.
    """
    final_state = await app.state.graph.ainvoke(
        {"request": request, "response": None, "diagnosis": None}
    )
    return final_state["response"].model_copy(update={"diagnosis": final_state["diagnosis"]})
