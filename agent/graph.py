"""
LangGraph wiring. GROUP-OWNED — keep this file small.

This file says which agents exist and in what order they run. It must not contain agent
logic, prompt text, tool names or parsing. All of that belongs in agents/<name>.py, owned
by one person, so that adding an agent is one node and one edge here and a new file there
instead of four people editing the same function.

Two routing rules, both plain Python reading the state — never a judgement made by a model:

    START -> clarify -> diagnose -> strategize -> END     a fresh report, nothing to ask
    START -> clarify -> END                               a fresh report, questions asked
    START -> diagnose -> strategize -> END                the reporter has answered
    START -> verify -> END                                a completed repair

`_route_from_start` reads what the request carries. A verification is a different question
about a different thing, so it never runs in line with the report pipeline: appended after
`strategize` it would re-clarify and re-diagnose a fault somebody has already repaired.
Clarification answers mean the clarifier has already asked, so the run resumes at the
diagnostic — sending it to `clarify` again would ask the same questions and loop.

`_route_after_clarify` is the first human pause. A clarifier that asked anything, or that
could not produce questions at all, ends the run: a diagnosis made before the answers would
be made without the detail the clarifier just said it needed. The API records the pause as
AwaitingClarification and calls /run again with the answers.

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
from schemas import (
    AgentStatus,
    DiagnosticResult,
    RunRequest,
    RunResponse,
    StrategistResult,
    VerificationResult,
)


class GraphState(TypedDict):
    """What flows between nodes. One entry per agent output."""

    request: RunRequest
    response: RunResponse | None
    diagnosis: DiagnosticResult | None
    strategy: StrategistResult | None
    verification: VerificationResult | None


def _route_from_start(state: GraphState) -> str:
    """A deterministic rule: the request says which question it is asking, and how far along."""
    request = state["request"]
    if request.verification is not None:
        return "verify"
    if request.clarification_answers:
        return "diagnose"
    return "clarify"


def _route_after_clarify(state: GraphState) -> str:
    """Human pause 1: carry on only when the clarifier ran cleanly and asked nothing."""
    response = state["response"]
    if response.status is AgentStatus.ok and not response.output.questions:
        return "diagnose"
    return END


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
    builder.add_conditional_edges(START, _route_from_start, ["clarify", "diagnose", "verify"])
    builder.add_conditional_edges("clarify", _route_after_clarify, ["diagnose", END])
    builder.add_edge("diagnose", "strategize")
    builder.add_edge("strategize", END)
    builder.add_edge("verify", END)

    return builder.compile()
