"""
LangGraph wiring. GROUP-OWNED — keep this file small.

This file says which agents exist and in what order they run. It must not contain agent
logic, prompt text, tool names or parsing. All of that belongs in agents/<name>.py, owned
by one person, so that adding an agent is one node and one edge here and a new file there
instead of four people editing the same function.

    START -> plan -> clarify -> diagnose -> strategize -> END   planned with the clarifier,
                                                               and nothing to ask
    START -> plan -> clarify -> END                            planned with the clarifier,
                                                               questions asked
    START -> plan -> diagnose -> strategize -> END             planned without the clarifier
    START -> diagnose -> strategize -> END                     the reporter has answered, or
                                                               a repair was reopened
    START -> strategize -> END                                 a manager sent the proposal
                                                               back for revision
    START -> verify -> END                                     a completed repair

`_route_from_start` reads what the request carries. A verification is a different question
about a different thing, so it never runs in line with the report pipeline and is never
planned. Clarification answers mean the clarifier has already asked, so the run resumes at the
diagnostic under the plan the API already stored — sending it to `clarify` again would ask
the same questions and loop. A reopened repair resumes there too: the fault was clarified and
repaired once already, and what is new since the diagnostic reads through its tools. A
revision note means a manager sent the proposal back: the fault is diagnosed already, so only
the strategist runs again, with the note.

`_route_after_plan` is where the plan DELEGATES. It reads the planner's plan — which
PlannerOutput has already checked is a legal one — and follows it: to the clarifier when the
plan includes it, straight to the diagnostic when it does not. A planner that failed produced
no plan, and the run takes the full pipeline, clarifier first: asking is the safe default.
The routing itself is still plain Python reading validated state, never a model's free text.

`_route_after_clarify` is the first human pause. A clarifier that asked anything, or that
could not produce questions at all, ends the run: a diagnosis made before the answers would
be made without the detail the clarifier just said it needed. The API records the pause as
AwaitingClarification and calls /run again with the answers.

Every node's result is stamped with how long its agent ran (`duration_ms`), so the API can
record each agent's own time rather than dividing the call's.
"""

from __future__ import annotations

import time
from typing import Awaitable, TypedDict, TypeVar

from langgraph.graph import END, START, StateGraph

from agents.clarifier import ClarifierAgent
from agents.diagnostic import DiagnosticAgent
from agents.planner import PlannerAgent
from agents.strategist import ResolutionStrategist
from agents.verification import VerificationAgent
from schemas import (
    AgentStatus,
    DiagnosticResult,
    PlannerResult,
    RunRequest,
    RunResponse,
    StrategistResult,
    VerificationResult,
)

_Result = TypeVar("_Result")


class GraphState(TypedDict, total=False):
    """What flows between nodes. One entry per agent output."""

    request: RunRequest
    plan: PlannerResult | None
    response: RunResponse | None
    diagnosis: DiagnosticResult | None
    strategy: StrategistResult | None
    verification: VerificationResult | None


def _route_from_start(state: GraphState) -> str:
    """A deterministic rule: the request says which question it is asking, and how far along."""
    request = state["request"]
    if request.verification is not None:
        return "verify"
    if request.revision_note is not None:
        return "strategize"
    if request.clarification_answers or request.reopened:
        return "diagnose"
    return "plan"


def _route_after_plan(state: GraphState) -> str:
    """The plan delegates: the clarifier only when the (already validated) plan includes it."""
    plan = state.get("plan")
    if plan is not None and plan.status is AgentStatus.ok and plan.output is not None:
        return "clarify" if plan.output.includes_clarifier else "diagnose"
    # No usable plan: the full pipeline, clarifier first — the safe default.
    return "clarify"


def _route_after_clarify(state: GraphState) -> str:
    """Human pause 1: carry on only when the clarifier ran cleanly and asked nothing."""
    response = state["response"]
    if response.status is AgentStatus.ok and not response.output.questions:
        return "diagnose"
    return END


async def _timed(run: Awaitable[_Result]) -> _Result:
    """Runs one agent and stamps its result with how long it took, in whole milliseconds."""
    started = time.perf_counter()
    result = await run
    if result is not None:
        # Set on the agent's own result rather than a copy: it is a fresh object every run,
        # and the next node is handed exactly what this one produced.
        result.duration_ms = int((time.perf_counter() - started) * 1000)
    return result


def build_graph(
    clarifier: ClarifierAgent,
    diagnostic: DiagnosticAgent,
    strategist: ResolutionStrategist,
    verifier: VerificationAgent,
    planner: PlannerAgent,
):
    """Compiles the workflow graph. Agents are injected so tests can supply stubs."""

    async def plan(state: GraphState) -> dict[str, PlannerResult]:
        return {"plan": await _timed(planner.run(state["request"]))}

    async def clarify(state: GraphState) -> dict[str, RunResponse]:
        return {"response": await _timed(clarifier.run(state["request"]))}

    async def diagnose(state: GraphState) -> dict[str, DiagnosticResult]:
        return {"diagnosis": await _timed(diagnostic.run(state["request"]))}

    async def strategize(state: GraphState) -> dict[str, StrategistResult]:
        return {"strategy": await _timed(strategist.run(state["request"], state.get("diagnosis")))}

    async def verify(state: GraphState) -> dict[str, VerificationResult]:
        return {"verification": await _timed(verifier.run(state["request"]))}

    builder = StateGraph(GraphState)
    builder.add_node("plan", plan)
    builder.add_node("clarify", clarify)
    builder.add_node("diagnose", diagnose)
    builder.add_node("strategize", strategize)
    builder.add_node("verify", verify)
    builder.add_conditional_edges(START, _route_from_start, ["plan", "diagnose", "strategize", "verify"])
    builder.add_conditional_edges("plan", _route_after_plan, ["clarify", "diagnose"])
    builder.add_conditional_edges("clarify", _route_after_clarify, ["diagnose", END])
    builder.add_edge("diagnose", "strategize")
    builder.add_edge("strategize", END)
    builder.add_edge("verify", END)

    return builder.compile()
