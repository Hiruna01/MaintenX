"""
Pydantic models for every agent's input and output.

These are the contract. The model is asked for JSON in a prompt and its reply is
validated against the schema here — nothing downstream ever sees model output that has
not passed through one of these classes.

Read the ClarifierOutput docstring before adding a field: the absence of a free-text
message field is a design constraint, not an oversight.
"""

from __future__ import annotations

from enum import Enum
from typing import Annotated, Any

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, model_validator

# A clarifier round asks at most two questions. Enforced by the schema, so an
# over-eager model reply fails validation instead of reaching a user.
MAX_QUESTIONS = 2

# Longest free-text answer a reporter may give to a short_text question.
MAX_SHORT_TEXT_ANSWER = 100

# Bounds on a single_select question, so the UI always has a sane radio group.
MIN_SELECT_OPTIONS = 2
MAX_SELECT_OPTIONS = 5

# Longest question a clarifier may ask. Shared by the question and by the answer that
# echoes it back into the diagnostic's input.
MAX_QUESTION_TEXT = 300

# How many earlier answers a run may carry. A report can be clarified by more than one
# run over its life, so this is well above MAX_QUESTIONS — but it is still a ceiling on
# how much reporter-typed text can reach a prompt.
MAX_CLARIFICATION_ANSWERS = 10

# A diagnosis offers between one and three candidate causes. Zero is not a diagnosis, and
# more than three is a list nobody reads.
MIN_HYPOTHESES = 1
MAX_HYPOTHESES = 3

# Each hypothesis cites between one and five pieces of evidence, each a short string.
MAX_EVIDENCE_ITEMS = 5
MAX_EVIDENCE_LENGTH = 200
MAX_CAUSE_LENGTH = 200
MAX_REASONING_SUMMARY = 400


class AnswerType(str, Enum):
    """How the client should render the answer control for a question."""

    yes_no = "yes_no"
    single_select = "single_select"
    short_text = "short_text"


class AgentStatus(str, Enum):
    """Whether an agent produced real output or fell back to its safe failure."""

    ok = "ok"
    safe_failure = "safe_failure"


class ClarifyingQuestion(BaseModel):
    """
    One question put to the reporter. Every answer is a closed control or a short
    capped string — never an open message box.
    """

    model_config = ConfigDict(extra="forbid")

    question_text: str = Field(min_length=1, max_length=MAX_QUESTION_TEXT)
    answer_type: AnswerType

    # Required for single_select, forbidden otherwise. See the validator below.
    options: list[str] | None = None

    # Set by us, not by the model: short_text answers are capped at 100 characters.
    max_answer_length: int | None = Field(default=None, ge=1, le=MAX_SHORT_TEXT_ANSWER)

    @model_validator(mode="after")
    def _check_answer_shape(self) -> "ClarifyingQuestion":
        if self.answer_type is AnswerType.single_select:
            count = len(self.options or [])
            if not (MIN_SELECT_OPTIONS <= count <= MAX_SELECT_OPTIONS):
                raise ValueError(
                    f"answer_type 'single_select' needs between {MIN_SELECT_OPTIONS} and "
                    f"{MAX_SELECT_OPTIONS} options, got {count}"
                )
        elif self.options:
            raise ValueError(f"answer_type '{self.answer_type.value}' must not carry options")

        # Normalised here rather than trusted from the model: the cap is our rule.
        self.max_answer_length = (
            MAX_SHORT_TEXT_ANSWER if self.answer_type is AnswerType.short_text else None
        )
        return self


class ClarifierOutput(BaseModel):
    """
    The clarifier's entire output: nothing but questions.

    HARD DESIGN CONSTRAINT — do not add to this model:
      * no free-text `message` / `reply` / `response` field,
      * no conversation history,
      * no follow-up or next-turn field.
    The system asks one round of closed questions and stops. Adding any of the above
    turns this into a chatbot, which this project deliberately does not have.

    An empty list is a valid, meaningful answer: nothing needs clarifying.
    """

    model_config = ConfigDict(extra="forbid")

    questions: list[ClarifyingQuestion] = Field(
        default_factory=list,
        max_length=MAX_QUESTIONS,
    )

    @classmethod
    def stub_example(cls) -> dict[str, Any]:
        """
        Fixed valid JSON returned by the LLM client in STUB_MODE, so tests never make a
        network call. Kept next to the schema so it cannot drift away from it.
        """
        return {
            "questions": [
                {
                    "question_text": "Is the equipment completely unresponsive?",
                    "answer_type": "yes_no",
                },
                {
                    "question_text": "How long has the problem been happening?",
                    "answer_type": "single_select",
                    "options": ["Today", "This week", "Longer than a week"],
                },
            ]
        }


class Confidence(str, Enum):
    """How strongly the evidence supports one hypothesis. See the rubric in diagnostic.md."""

    high = "high"
    medium = "medium"
    low = "low"


class NextAction(str, Enum):
    """
    What the diagnostic suggests happens next. A closed list so the reply can be
    validated — NOT so the system can act on it.

    This is the model's opinion, recorded for a human. Nothing downstream may treat
    "replace" as a decision: whether money is spent is approval routing, and approval
    routing is a deterministic business rule that lives in C#. Same reasoning as
    VerificationCheck.AgentOutcome on the API side.
    """

    inspect = "inspect"
    repair = "repair"
    replace = "replace"
    monitor = "monitor"


class ClarificationAnswer(BaseModel):
    """
    One answer the reporter gave to an earlier clarifier question, echoed back with the
    question so the diagnostic knows what was asked.

    The answer is always text — "Yes", the chosen option, or the short free text — capped
    at the same 100 characters as the API's ClarificationAnswer.AnswerText. It is typed by
    a member of the public, so the diagnostic treats it exactly like the report itself:
    as data, never as instructions.
    """

    model_config = ConfigDict(extra="forbid")

    question_text: str = Field(min_length=1, max_length=MAX_QUESTION_TEXT)
    answer_text: str = Field(min_length=1, max_length=MAX_SHORT_TEXT_ANSWER)


class DiagnosticInput(BaseModel):
    """
    Everything the diagnostic agent is allowed to see, and nothing else.

    A projection of RunRequest rather than RunRequest itself, on purpose: the prompt is
    rendered from THIS model, so a field added to RunRequest for some other agent does
    not silently start reaching the diagnostic's prompt. It is the data equivalent of
    DiagnosticAgent.ALLOWED_TOOLS.

    There is no conversation history field and there never will be — `extra="forbid"`
    makes a stray `conversation_history` a validation error rather than something quietly
    ignored. The diagnostic runs one round per report.

    `room_id` is carried because it is part of the report, but it is not looked up: the
    diagnostic does not have get_room, and get_asset already returns the room's name.
    """

    model_config = ConfigDict(extra="forbid")

    # Same bounds as RunRequest.description, so building this from a valid RunRequest
    # can never fail — the agent's run() must not raise.
    description: str = Field(min_length=1, max_length=4000)
    room_id: int | None = None
    asset_id: int | None = None
    clarification_answers: list[ClarificationAnswer] = Field(
        default_factory=list,
        max_length=MAX_CLARIFICATION_ANSWERS,
    )

    @classmethod
    def from_run_request(cls, request: "RunRequest") -> "DiagnosticInput":
        return cls(
            description=request.description,
            room_id=request.room_id,
            asset_id=request.asset_id,
            clarification_answers=request.clarification_answers,
        )


EvidenceItem = Annotated[str, StringConstraints(min_length=1, max_length=MAX_EVIDENCE_LENGTH)]


class Hypothesis(BaseModel):
    """
    One candidate cause, with how sure the model is and what it is going on.

    `evidence` must have at least one entry. A hypothesis with nothing behind it is an
    invented cause, which is exactly what the prompt forbids — so when there is no service
    history the model has to SAY so in evidence ("No service history on record") rather
    than leave the list empty and let the cause stand on nothing.
    """

    model_config = ConfigDict(extra="forbid")

    cause: str = Field(min_length=1, max_length=MAX_CAUSE_LENGTH)
    confidence: Confidence
    evidence: list[EvidenceItem] = Field(min_length=1, max_length=MAX_EVIDENCE_ITEMS)


class DiagnosticOutput(BaseModel):
    """
    The diagnostic's entire output.

    HARD DESIGN CONSTRAINT, the same as ClarifierOutput — do not add to this model:
      * no free-text `message` / `reply` / `response` field,
      * no conversation history,
      * no follow-up or next-turn field.
    One round, four fields, and the exchange is over. Pinned by a test that asserts
    `model_fields` is exactly this set.

    `reasoning_summary` is capped at 400 characters and is not a message to anyone: it is
    the model's account of why, recorded for the human who decides, and there is no reply
    to it because there is nobody to reply to.
    """

    model_config = ConfigDict(extra="forbid")

    hypotheses: list[Hypothesis] = Field(min_length=MIN_HYPOTHESES, max_length=MAX_HYPOTHESES)
    primary_hypothesis_index: int = Field(ge=0)
    recommended_next_action: NextAction
    reasoning_summary: str = Field(min_length=1, max_length=MAX_REASONING_SUMMARY)

    @model_validator(mode="after")
    def _check_primary_index(self) -> "DiagnosticOutput":
        # Checked here rather than trusted: an index past the end of the list would point
        # a reader at a hypothesis that does not exist.
        if self.primary_hypothesis_index >= len(self.hypotheses):
            raise ValueError(
                f"primary_hypothesis_index {self.primary_hypothesis_index} is out of range "
                f"for {len(self.hypotheses)} hypotheses"
            )
        return self

    @classmethod
    def stub_example(cls) -> dict[str, Any]:
        """
        Fixed valid JSON returned by the LLM client in STUB_MODE. Kept next to the schema
        so it cannot drift away from it.

        Deliberately NOT evidence of anything: it is a fixed reply, so no test may use it
        to claim the model reasons correctly. That is what agent/evals/ is for.
        """
        return {
            "hypotheses": [
                {
                    "cause": "Overheating from a clogged air filter and a weakening cooling fan",
                    "confidence": "high",
                    "evidence": [
                        "2026-07-03: air filter choked with dust, lamp hours high",
                        "2026-09-02: still running hot after filter clean, fan bearing weak",
                    ],
                },
                {
                    "cause": "Intermittent HDMI connection",
                    "confidence": "low",
                    "evidence": ["2026-05-12: cable reseated, no fault seen on test"],
                },
            ],
            "primary_hypothesis_index": 0,
            "recommended_next_action": "inspect",
            "reasoning_summary": (
                "Two temporary fixes for the same thermal fault within two months, and the "
                "last technician reported a weak fan bearing."
            ),
        }


class DiagnosticResult(BaseModel):
    """
    The diagnostic agent's envelope: its status, its output, and the tools it called.

    `output` is None on a safe failure — unlike the clarifier, whose safe failure is an
    empty question list. The difference is deliberate. Asking nothing is a real, safe
    answer; there is no such thing as an empty diagnosis, and inventing a placeholder
    ("unknown cause — inspect") would be a recommendation nobody made. No diagnosis means
    a human looks at the report, which is exactly what should happen.
    """

    agent: str
    status: AgentStatus
    output: DiagnosticOutput | None = None
    error: str | None = None
    tool_calls: list[ToolCallOutcome] = Field(default_factory=list)


class ToolCallOutcome(BaseModel):
    """
    The result of one call to the API's tool router.

    `found` mirrors the API's own distinction: the tool ran and there was no such row
    (found=False) is not the same as the tool being refused (error set).

    `result` is a dict for the single-row tools (get_room, get_asset) and a LIST for
    get_asset_service_history and get_related_open_reports. An empty list with found=True
    is an answer — "never serviced", "nothing open" — and is not the same as found=False,
    which means the asset itself was not there.
    """

    tool: str
    found: bool
    result: dict[str, Any] | list[Any] | None = None
    error: str | None = None


class RunRequest(BaseModel):
    """Input to POST /run. Sent by the ASP.NET Core API, never by a browser."""

    model_config = ConfigDict(extra="forbid")

    workflow_id: int
    description: str = Field(min_length=1, max_length=4000)

    # Optional context ids the agent may look up through allow-listed tools.
    room_id: int | None = None
    building_id: int | None = None

    # Null in the normal case: a reporter is not expected to know which asset tag the
    # projector carries. Filled in by a QR scan or at triage — see Report.AssetId.
    asset_id: int | None = None

    # The reporter's answers to questions a PREVIOUS run asked. Empty on a report's first
    # run, because the clarifier and the diagnostic run in the same graph and nobody has
    # answered anything yet.
    clarification_answers: list[ClarificationAnswer] = Field(
        default_factory=list,
        max_length=MAX_CLARIFICATION_ANSWERS,
    )


class RunResponse(BaseModel):
    """
    Output of POST /run.

    `status` is always present: a safe failure is a normal 200 response carrying empty
    output, never an exception and never a 500. The caller polls a workflow, so a dead
    agent must still produce a well-formed answer.
    """

    workflow_id: int
    agent: str
    status: AgentStatus
    output: ClarifierOutput
    error: str | None = None
    tool_calls: list[ToolCallOutcome] = Field(default_factory=list)

    # The diagnostic's result, when the graph ran it. A separate field rather than a
    # change to the ones above: the top-level fields are the clarifier's and the API's
    # AgentRunResponse reads them by name, so a second agent is an addition to this
    # contract, never a reinterpretation of it.
    diagnosis: DiagnosticResult | None = None
