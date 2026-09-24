"""
LangGraph wiring. GROUP-OWNED — keep this file small.

This file says which agents exist and in what order they run. It must not contain agent
logic, prompt text, tool names or parsing. All of that belongs in agents/<name>.py, owned
by one person, so that adding an agent is one node and one edge here and a new file there
instead of four people editing the same function.

Today the graph is two paths from one routing rule:

    START -> clarify -> diagnose -> strategize -> END     a report
    START -> verify -> END                                 a completed repair

A report and a verification are different questions about different things, so they do
not run in one line: a verification appended after `strategize` would re-clarify and
re-diagnose a fault somebody has already repaired, and the clarifier would put questions
to the reporter about it again. Which path runs is `_route_from_start` — plain Python
reading whether the request carries a verification, never a judgement made by a model.

When the next agent lands, add its node and move the edge. Any other routing decision
goes in `add_conditional_edges` the same way.
"""

from __future__ import annotations

from typing import TypedDict

from langgraph.graph import END, START, StateGraph

from agents.clarifier import ClarifierAgent
from agents.diagnostic import DiagnosticAgent
from agents.strategist import ResolutionStrategist
from agents.verification import VerificationAgent
from schemas import DiagnosticResult, RunRequest, RunResponse, StrategistResult, VerificationResult


class GraphState(TypedDict):
    """What flows between nodes. One entry per agent output."""

    request: RunRequest
    response: RunResponse | None
    diagnosis: DiagnosticResult | None
    strategy: StrategistResult | None
    verification: VerificationResult | None


def _route_from_start(state: GraphState) -> str:
    """A deterministic rule: the request says which question it is asking."""
    return "verify" if state["request"].verification is not None else "clarify"


def build_graph(
    clarifier: ClarifierAgent,
    diagnostic: DiagnosticAgent,
    strategist: ResolutionStrategist,
    verifier: VerificationAgent,
):
    """Compiles the workflow graph. Agents are injected so tests can supply stubs."""

    async def clarify(state: GraphState) -> dict[str, RunResponse]:
        return {"response": await clarifier.run(state["request"])}

    async def diagnose(state: GraphState) -> dict[str, DiagnosticResult]:
        return {"diagnosis": await diagnostic.run(state["request"])}

    async def strategize(state: GraphState) -> dict[str, StrategistResult]:
        return {"strategy": await strategist.run(state["request"], state["diagnosis"])}

    async def verify(state: GraphState) -> dict[str, VerificationResult]:
        return {"verification": await verifier.run(state["request"])}

    builder = StateGraph(GraphState)
    builder.add_node("clarify", clarify)
    builder.add_node("diagnose", diagnose)
    builder.add_node("strategize", strategize)
    builder.add_node("verify", verify)
    builder.add_conditional_edges(START, _route_from_start, ["clarify", "verify"])
    builder.add_edge("clarify", "diagnose")
    builder.add_edge("diagnose", "strategize")
    builder.add_edge("strategize", END)
    builder.add_edge("verify", END)

    return builder.compile()
