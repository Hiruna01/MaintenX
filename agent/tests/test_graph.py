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
    PlannerOutput,
    PlannerResult,
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


def _plan(*agents: str, status: AgentStatus = AgentStatus.ok) -> PlannerResult:
    """A planner reply delegating to `agents`, in order — or no plan at all on a safe failure."""
    if status is not AgentStatus.ok:
        return PlannerResult(agent="planner", status=status, error="spy")
    return PlannerResult(
        agent="planner",
        status=AgentStatus.ok,
        output=PlannerOutput(
            steps=[{"agent": agent, "purpose": f"{agent} for this report"} for agent in agents],
            rationale="spy plan",
        ),
    )


FULL_PLAN = ("clarifier", "diagnostic", "strategist")


def _graph(clarifier_reply: RunResponse, plan: PlannerResult | None = None):
    calls: list[str] = []
    diagnosis = DiagnosticResult(agent="diagnostic", status=AgentStatus.safe_failure, error="spy")
    spies = (
        _Spy("clarify", clarifier_reply, calls),
        _Spy("diagnose", diagnosis, calls),
        _Spy("strategize", StrategistResult(agent="strategist", status=AgentStatus.safe_failure), calls),
        _Spy("verify", None, calls),
        _Spy("plan", plan if plan is not None else _plan(*FULL_PLAN), calls),
    )
    return build_graph(*spies), spies, calls


def _initial(request: RunRequest) -> dict:
    return {
        "request": request,
        "plan": None,
        "response": None,
        "diagnosis": None,
        "strategy": None,
        "verification": None,
    }


FRESH = RunRequest(workflow_id=50, description="Projector cutting out.")


async def test_a_clarifier_that_asks_nothing_hands_on_to_the_diagnostic_then_the_strategist():
    graph, (_, diagnostic, strategist, _, _), calls = _graph(_clarifier_reply(questions=0))

    state = await graph.ainvoke(_initial(FRESH))

    assert calls == ["plan", "clarify", "diagnose", "strategize"]
    # The strategist is handed the diagnosis the diagnostic produced, not a fresh lookup.
    assert strategist.args[1] is diagnostic.result
    assert state["strategy"] is strategist.result


async def test_a_clarifier_that_asks_anything_ends_the_run_there():
    """Human pause 1: a diagnosis before the answers would be made without them."""
    graph, _, calls = _graph(_clarifier_reply(questions=2))

    state = await graph.ainvoke(_initial(FRESH))

    assert calls == ["plan", "clarify"]
    assert state["diagnosis"] is None and state["strategy"] is None


async def test_a_clarifier_that_safe_failed_ends_the_run_there():
    """No questions because it could not produce any is not "nothing to ask"."""
    graph, _, calls = _graph(_clarifier_reply(status=AgentStatus.safe_failure))

    await graph.ainvoke(_initial(FRESH))

    assert calls == ["plan", "clarify"]


async def test_a_run_carrying_answers_resumes_at_the_diagnostic_without_asking_again():
    """Sent to the clarifier again, it would ask the same questions and loop."""
    graph, (clarifier, diagnostic, _, _, _), calls = _graph(_clarifier_reply(questions=2))
    answered = FRESH.model_copy(
        update={"clarification_answers": [ClarificationAnswer(question_text="Question 0?", answer_text="Yes")]}
    )

    state = await graph.ainvoke(_initial(answered))

    assert calls == ["diagnose", "strategize"]
    # The answers reach the diagnostic on the request it is handed.
    assert diagnostic.args[0].clarification_answers == answered.clarification_answers
    assert state["response"] is None


async def test_a_reopened_repair_is_diagnosed_again_without_asking_again():
    """
    Verification reopened the repair, and the run carries no answers of its own. Routed to
    the clarifier, it would put questions to the reporter about a fault somebody already
    repaired; it goes straight to the diagnostic, and on to the strategist for a new proposal.
    """
    graph, (_, diagnostic, _, _, _), calls = _graph(_clarifier_reply(questions=2))
    reopened = FRESH.model_copy(update={"reopened": True})

    state = await graph.ainvoke(_initial(reopened))

    assert calls == ["diagnose", "strategize"]
    assert diagnostic.args[0].reopened is True
    assert state["response"] is None


async def test_a_revision_runs_the_strategist_alone_with_the_managers_note():
    """
    A manager sent the proposal back. The fault is diagnosed already, so neither the planner,
    the clarifier nor the diagnostic runs again — only the strategist, handed the note on the
    request and no fresh diagnosis.
    """
    graph, (_, _, strategist, _, _), calls = _graph(_clarifier_reply(questions=2))
    revision = FRESH.model_copy(
        update={"revision_note": "Too expensive - price a repair first.", "revision_work_order_id": 57}
    )

    state = await graph.ainvoke(_initial(revision))

    assert calls == ["strategize"]
    assert strategist.args[0].revision_note == "Too expensive - price a repair first."
    assert strategist.args[1] is None
    assert state["response"] is None and state["diagnosis"] is None


# ----------------------------------------------------------------------
# The plan delegates. What the planner decides is whether the clarifier runs; the routing on
# that decision is plain Python reading a plan PlannerOutput has already validated.
# ----------------------------------------------------------------------


async def test_a_plan_without_the_clarifier_goes_straight_to_the_diagnostic():
    """The planner judged the report clear: nobody is asked anything."""
    graph, (clarifier, _, _, _, _), calls = _graph(
        _clarifier_reply(questions=2), plan=_plan("diagnostic", "strategist")
    )

    state = await graph.ainvoke(_initial(FRESH))

    assert calls == ["plan", "diagnose", "strategize"]
    assert state["response"] is None
    assert state["plan"].output.includes_clarifier is False


async def test_a_planner_that_failed_falls_back_to_the_full_pipeline_clarifier_first():
    """No plan is not a plan to skip anything: asking is the safe default."""
    graph, _, calls = _graph(_clarifier_reply(questions=0), plan=_plan(status=AgentStatus.safe_failure))

    await graph.ainvoke(_initial(FRESH))

    assert calls == ["plan", "clarify", "diagnose", "strategize"]


async def test_a_resumed_run_is_not_planned_again():
    """It follows the plan the API already stored; a second plan would be a second opinion on it."""
    graph, _, calls = _graph(_clarifier_reply(questions=2))
    answered = FRESH.model_copy(
        update={"clarification_answers": [ClarificationAnswer(question_text="Question 0?", answer_text="Yes")]}
    )

    await graph.ainvoke(_initial(answered))

    assert "plan" not in calls


async def test_every_agent_that_ran_is_stamped_with_its_own_duration():
    graph, _, _ = _graph(_clarifier_reply(questions=0))

    state = await graph.ainvoke(_initial(FRESH))

    for key in ("plan", "response", "diagnosis", "strategy"):
        assert isinstance(state[key].duration_ms, int) and state[key].duration_ms >= 0
