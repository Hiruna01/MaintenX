"""
The report pipeline's routing in graph.py — which agents run, in what order, and where the
run stops. Spy agents stand in for the real ones, so what is tested is the wiring alone;
each agent's own behaviour is tested in its own file.

The verification path's routing is pinned in test_verification.py.
"""

from __future__ import annotations

from graph import build_graph
from schemas import (
    AgentStatus,
    ClarificationAnswer,
    DiagnosticResult,
    RunRequest,
    RunResponse,
    StrategistResult,
)


class _Spy:
    """Stands in for an agent: records that it ran, in order, and what it was handed."""

    def __init__(self, name: str, result, calls: list[str]) -> None:
        self.name = name
        self.result = result
        self.calls = calls
        self.args: tuple = ()

    async def run(self, *args):
        self.calls.append(self.name)
        self.args = args
        return self.result


def _clarifier_reply(status: AgentStatus = AgentStatus.ok, questions: int = 0) -> RunResponse:
    return RunResponse(
        workflow_id=50,
        agent="clarifier",
        status=status,
        output={
            "questions": [
                {"question_text": f"Question {i}?", "answer_type": "yes_no"} for i in range(questions)
            ]
        },
    )


def _graph(clarifier_reply: RunResponse):
    calls: list[str] = []
    diagnosis = DiagnosticResult(agent="diagnostic", status=AgentStatus.safe_failure, error="spy")
    spies = (
        _Spy("clarify", clarifier_reply, calls),
        _Spy("diagnose", diagnosis, calls),
        _Spy("strategize", StrategistResult(agent="strategist", status=AgentStatus.safe_failure), calls),
        _Spy("verify", None, calls),
    )
    return build_graph(*spies), spies, calls


def _initial(request: RunRequest) -> dict:
    return {"request": request, "response": None, "diagnosis": None, "strategy": None, "verification": None}


FRESH = RunRequest(workflow_id=50, description="Projector cutting out.")


async def test_a_clarifier_that_asks_nothing_hands_on_to_the_diagnostic_then_the_strategist():
    graph, (_, diagnostic, strategist, _), calls = _graph(_clarifier_reply(questions=0))

    state = await graph.ainvoke(_initial(FRESH))

    assert calls == ["clarify", "diagnose", "strategize"]
    # The strategist is handed the diagnosis the diagnostic produced, not a fresh lookup.
    assert strategist.args[1] is diagnostic.result
    assert state["strategy"] is strategist.result


async def test_a_clarifier_that_asks_anything_ends_the_run_there():
    """Human pause 1: a diagnosis before the answers would be made without them."""
    graph, _, calls = _graph(_clarifier_reply(questions=2))

    state = await graph.ainvoke(_initial(FRESH))

    assert calls == ["clarify"]
    assert state["diagnosis"] is None and state["strategy"] is None


async def test_a_clarifier_that_safe_failed_ends_the_run_there():
    """No questions because it could not produce any is not "nothing to ask"."""
    graph, _, calls = _graph(_clarifier_reply(status=AgentStatus.safe_failure))

    await graph.ainvoke(_initial(FRESH))

    assert calls == ["clarify"]


async def test_a_run_carrying_answers_resumes_at_the_diagnostic_without_asking_again():
    """Sent to the clarifier again, it would ask the same questions and loop."""
    graph, (clarifier, diagnostic, _, _), calls = _graph(_clarifier_reply(questions=2))
    answered = FRESH.model_copy(
        update={"clarification_answers": [ClarificationAnswer(question_text="Question 0?", answer_text="Yes")]}
    )

    state = await graph.ainvoke(_initial(answered))

    assert calls == ["diagnose", "strategize"]
    # The answers reach the diagnostic on the request it is handed.
    assert diagnostic.args[0].clarification_answers == answered.clarification_answers
    assert state["response"] is None
