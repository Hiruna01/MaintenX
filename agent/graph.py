"""
LangGraph wiring. GROUP-OWNED — keep this file small.

This file says which agents exist and in what order they run. It must not contain agent
logic, prompt text, tool names or parsing. All of that belongs in agents/<name>.py, owned
by one person, so that adding an agent is one node and one edge here and a new file there
instead of four people editing the same function.

Today the graph is two nodes, in order:

    START -> clarify -> diagnose -> END

When the next agent lands, add its node and move the edge. If routing ever needs a
decision, it goes in `add_conditional_edges` here as a plain Python function reading the
state — a deterministic rule in code, never a judgement made by a model.
"""

from __future__ import annotations

from typing import TypedDict

from langgraph.graph import END, START, StateGraph

from agents.clarifier import ClarifierAgent
from agents.diagnostic import DiagnosticAgent
from schemas import DiagnosticResult, RunRequest, RunResponse


class GraphState(TypedDict):
    """What flows between nodes. One entry per agent output."""

    request: RunRequest
    response: RunResponse | None
    diagnosis: DiagnosticResult | None


def build_graph(clarifier: ClarifierAgent, diagnostic: DiagnosticAgent):
    """Compiles the workflow graph. Agents are injected so tests can supply stubs."""

    async def clarify(state: GraphState) -> dict[str, RunResponse]:
        return {"response": await clarifier.run(state["request"])}

    async def diagnose(state: GraphState) -> dict[str, DiagnosticResult]:
        return {"diagnosis": await diagnostic.run(state["request"])}

    builder = StateGraph(GraphState)
    builder.add_node("clarify", clarify)
    builder.add_node("diagnose", diagnose)
    builder.add_edge(START, "clarify")
    builder.add_edge("clarify", "diagnose")
    builder.add_edge("diagnose", END)

    return builder.compile()
