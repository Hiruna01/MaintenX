"""
Pydantic models for every agent's input and output.

These are the contract. The model is asked for JSON in a prompt and its reply is
validated against the schema here — nothing downstream ever sees model output that has
not passed through one of these classes.

Read the ClarifierOutput docstring before adding a field: the absence of a free-text
message field is a design constraint, not an oversight.
"""

from __future__ import annotations

from decimal import Decimal
from enum import Enum
from typing import Annotated, Any

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    PlainSerializer,
    StringConstraints,
    model_validator,
)

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

# A strategy's justification, and a manager's revision note (the same 1000 characters as
# the API's WorkOrder.RevisionNote column).
MAX_JUSTIFICATION = 500
MAX_REVISION_NOTE = 1000

# The same bounds as the API's CreateWorkOrderDto.EstimatedCost, in LKR, to the cent.
MAX_ESTIMATED_COST = Decimal("10000000")

# How many open work orders a consolidated job may fold in — the API's
# IWorkOrderService.MaxToolOpenWorkOrders, since those are all the strategist is shown.
MAX_CONSOLIDATED_ORDERS = 10

# The verification agent's reason, and the reporter's comment it weighs — the same 300
# characters as the API's ReporterConfirmationDto.Comment.
MAX_VERIFICATION_REASON = 400
MAX_REPORTER_COMMENT = 300

# The API's row caps on the two list tools the verification agent reads:
# IAssetService.MaxToolHistoryRows and IReportService.MaxToolRelatedReports.
MAX_TOOL_HISTORY_ROWS = 20
MAX_TOOL_RELATED_REPORTS = 10

# Longest technician note or report description carried into the verification input.
# The API's own column limits; a longer one would mean the contract has drifted.
MAX_TECHNICIAN_NOTE = 2000
MAX_REPORT_DESCRIPTION = 4000


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


class Strategy(str, Enum):
    """
    How the strategist proposes the fault is handled. The same six members as the API's
    WorkOrderStrategy enum, in snake_case as every agent value is on the wire.
    """

    known_fix = "known_fix"
    single_job = "single_job"
    consolidated_job = "consolidated_job"
    inspect_first = "inspect_first"
    defer = "defer"
    escalate_replacement = "escalate_replacement"


class Urgency(str, Enum):
    """How soon the strategist thinks the work should happen. Advice, like everything here."""

    low = "low"
    medium = "medium"
    high = "high"


# Money: a Decimal to the cent, never a float — the same rule as the API's decimal
# columns. Pydantic reads a JSON number into a Decimal from its text, so 4999.99 arrives
# exactly and 4999.999 is REJECTED for its third place rather than rounded. It is written
# back out as a JSON number (not the string Pydantic would default to), which the API's
# System.Text.Json reads straight into a C# decimal without passing through a double.
Money = Annotated[
    Decimal,
    Field(ge=0, le=MAX_ESTIMATED_COST, decimal_places=2),
    PlainSerializer(float, return_type=float, when_used="json"),
]


class StrategistInput(BaseModel):
    """
    Everything the strategist is allowed to see, and nothing else — a projection, for the
    same reason as DiagnosticInput. The asset's record, history and the open work orders
    around it are fetched through the strategist's own tools, not passed in.

    `diagnosis` is None when the diagnostic produced nothing; the strategist is then told
    so plainly rather than handed a placeholder. `revision_note` is set only on a re-run
    after a manager sent a proposal back — see WorkOrder.RevisionNote on the API side.

    No conversation history, and `extra="forbid"` makes one a validation error.
    """

    model_config = ConfigDict(extra="forbid")

    description: str = Field(min_length=1, max_length=4000)
    asset_id: int | None = None
    diagnosis: DiagnosticOutput | None = None
    revision_note: str | None = Field(default=None, min_length=1, max_length=MAX_REVISION_NOTE)

    @classmethod
    def from_run(
        cls,
        request: "RunRequest",
        diagnosis: "DiagnosticResult | None",
    ) -> "StrategistInput":
        return cls(
            description=request.description,
            asset_id=request.asset_id,
            diagnosis=diagnosis.output if diagnosis is not None else None,
            revision_note=request.revision_note,
        )


class StrategistOutput(BaseModel):
    """
    The strategist's entire output: a PROPOSAL.

    HARD DESIGN CONSTRAINT — there is no approval field, and there must never be one. No
    `approved`, no `status`, no `requires_approval`. Whether a work order needs a manager's
    decision is the API comparing `estimated_cost` against Approval:CostThreshold in C#,
    plus the rule that escalate_replacement always goes to a manager. An agent that could
    say "approved" would be the approval control removed by the thing it is supposed to
    control. Pinned by a test that asserts `model_fields` is exactly these five, and
    `extra="forbid"` makes a reply that adds one a validation failure.

    The same no-chat rule as the other agents: no message, no history, no follow-up.
    `justification` is the model's account of why, for the manager who decides, and there
    is no reply to it.
    """

    model_config = ConfigDict(extra="forbid")

    strategy: Strategy
    estimated_cost: Money
    urgency: Urgency
    justification: str = Field(min_length=1, max_length=MAX_JUSTIFICATION)
    consolidate_with_work_order_ids: list[Annotated[int, Field(gt=0)]] = Field(
        default_factory=list,
        max_length=MAX_CONSOLIDATED_ORDERS,
    )

    @model_validator(mode="after")
    def _check_consolidation(self) -> "StrategistOutput":
        # Both directions. Ids on any other strategy would be a consolidation nobody
        # proposed; a consolidated job with none is a single job wearing the wrong name.
        ids = self.consolidate_with_work_order_ids
        if self.strategy is Strategy.consolidated_job:
            if not ids:
                raise ValueError("consolidated_job needs at least one work order id to consolidate with")
            if len(set(ids)) != len(ids):
                raise ValueError("consolidate_with_work_order_ids must not repeat an id")
        elif ids:
            raise ValueError(
                f"consolidate_with_work_order_ids must be empty unless strategy is "
                f"consolidated_job, got {self.strategy.value}"
            )
        return self

    @classmethod
    def stub_example(cls) -> dict[str, Any]:
        """
        Fixed valid JSON returned by the LLM client in STUB_MODE. Kept next to the schema
        so it cannot drift away from it.

        Deliberately NOT evidence of anything: a fixed reply cannot show the model chooses
        well. The golden case's behavioural half is agent/evals/test_strategist_live.py.
        """
        return {
            "strategy": "escalate_replacement",
            "estimated_cost": 185000.00,
            "urgency": "high",
            "justification": (
                "Same thermal fault three times since May: two temporary fixes and a weak "
                "fan bearing reported on 2026-09-02 with a replacement recommended. Another "
                "repair is likely to fail mid-term in the busiest lecture hall."
            ),
            "consolidate_with_work_order_ids": [],
        }


class StrategistResult(BaseModel):
    """
    The strategist's envelope. `output` is None on a safe failure, like the diagnostic's:
    no proposal is not the same as a proposal to defer, and a placeholder strategy would be
    a decision nobody made. No proposal means a manager decides from the report.
    """

    agent: str
    status: AgentStatus
    output: StrategistOutput | None = None
    error: str | None = None
    tool_calls: list[ToolCallOutcome] = Field(default_factory=list)


class VerificationOutcome(str, Enum):
    """
    What the verification agent makes of a completed repair. A closed list so the reply can
    be validated — NOT so the system can act on it.

    The API sets the check's Status from the REPORTER's answer, in C#; this label is stored
    beside it in VerificationCheck.AgentOutcome as a string, for a human to read. Same
    reasoning as NextAction: an enum here, an opinion there.

    `escalate` is not a stronger `reopen`. It says the pattern, not the one repair, is the
    problem — "this keeps happening, stop patching it".
    """

    confirm = "confirm"
    reopen = "reopen"
    escalate = "escalate"


class VerificationRequest(BaseModel):
    """
    What the API sends to ask for a verification: which repair, and what the reporter said.

    Its presence on RunRequest is what routes a /run to the verification agent instead of
    the report pipeline — see graph.py. Everything else the agent weighs (the resolution
    note, the history, the reports filed since) it looks up through its own tools, so the
    API cannot hand it a different account of the repair than the one on the record.

    `reporter_confirmed` is None when the reporter never answered: the sweep queues silent
    checks too, and silence is not a yes. Same rule as VerificationCheck.ReporterConfirmed.
    """

    model_config = ConfigDict(extra="forbid")

    work_order_id: int = Field(gt=0)
    reporter_confirmed: bool | None = None
    reporter_comment: str | None = Field(default=None, min_length=1, max_length=MAX_REPORTER_COMMENT)


class ServiceVisit(BaseModel):
    """One row of the asset's service history, as the verification agent is shown it."""

    model_config = ConfigDict(extra="forbid")

    serviced_on: str | None = None
    outcome: str | None = None
    technician_note: str | None = Field(default=None, max_length=MAX_TECHNICIAN_NOTE)

    # True for the record the repair being verified appended on completion. Marked by
    # code from the row's workOrderId, so the model never has to guess which visit is the
    # one it is judging.
    is_this_repair: bool = False


class NewReport(BaseModel):
    """A fault reported on the same asset AFTER the repair was completed."""

    model_config = ConfigDict(extra="forbid")

    reported_at: str | None = None
    status: str | None = None
    description: str = Field(min_length=1, max_length=MAX_REPORT_DESCRIPTION)


class VerificationInput(BaseModel):
    """
    Everything the verification agent is allowed to see, and nothing else. The prompt is
    rendered from THIS model, so nothing reaches it that was not declared here.

    Assembled by the agent from the request and its three tools, and every derived figure
    in it is worked out by CODE, never by the model: `days_since_completion` from the work
    order's completion date, `new_reports_since_completion` by comparing each report's
    timestamp with it, `service_visits_on_record` by counting the history's rows. The model
    judges what those facts mean; it does not produce them.

    NULL IS NOT EMPTY, as everywhere else: `new_reports_since_completion` is None when the
    lookup failed and [] when it answered "none", and the same for the history. `notes`
    says which lookups failed.

    No conversation history, and `extra="forbid"` makes one a validation error.
    """

    model_config = ConfigDict(extra="forbid")

    # The fault as it was first reported, so a new report can be read as the same fault
    # or a different one.
    fault_reported: str = Field(min_length=1, max_length=MAX_REPORT_DESCRIPTION)

    resolution_note: str | None = Field(default=None, max_length=MAX_TECHNICIAN_NOTE)
    days_since_completion: int | None = Field(default=None, ge=0)

    new_reports_since_completion: list[NewReport] | None = Field(
        default=None, max_length=MAX_TOOL_RELATED_REPORTS
    )

    reporter_confirmed: bool | None = None
    reporter_comment: str | None = Field(default=None, max_length=MAX_REPORTER_COMMENT)

    service_history_newest_first: list[ServiceVisit] | None = Field(
        default=None, max_length=MAX_TOOL_HISTORY_ROWS
    )

    # THE COUNT, from the tool's rows, so "how many times" is never the model's arithmetic.
    # At MAX_TOOL_HISTORY_ROWS it means "at least this many" — the tool is capped.
    service_visits_on_record: int | None = Field(default=None, ge=0)

    notes: list[str] = Field(default_factory=list)


class VerificationOutput(BaseModel):
    """
    The verification agent's entire output: a verdict on one repair, and why.

    HARD DESIGN CONSTRAINT, the same as every other agent — do not add to this model:
      * no free-text `message` / `reply` / `response` field,
      * no conversation history,
      * no follow-up or next-turn field.
    One round, four fields. Pinned by a test that asserts `model_fields` is exactly this
    set, and `extra="forbid"` makes a reply that adds one a validation failure.

    `reason` is the model's account of why, for the manager who reads it beside the
    reporter's answer. There is no reply to it. `evidence` needs at least one entry for the
    same reason a Hypothesis does: a verdict standing on nothing is an invented one.
    """

    model_config = ConfigDict(extra="forbid")

    outcome: VerificationOutcome
    confidence: Confidence
    reason: str = Field(min_length=1, max_length=MAX_VERIFICATION_REASON)
    evidence: list[EvidenceItem] = Field(min_length=1, max_length=MAX_EVIDENCE_ITEMS)

    @classmethod
    def stub_example(cls) -> dict[str, Any]:
        """
        Fixed valid JSON returned by the LLM client in STUB_MODE. Kept next to the schema
        so it cannot drift away from it.

        Deliberately NOT evidence of anything: a fixed reply cannot show the model judges
        well. The behavioural half is agent/evals/test_verification_live.py.
        """
        return {
            "outcome": "escalate",
            "confidence": "high",
            "reason": (
                "The reporter says it cut out twice again this week, and this is the fourth "
                "visit for the same thermal fault since May. Cleaning is not holding."
            ),
            "evidence": [
                "Reporter: not fixed - 'Cut out twice again this week. Same as before.'",
                "2026-07-03 and 2026-09-02: both recorded as temporary fixes",
                "4 service visits on record for this asset",
            ],
        }


class VerificationResult(BaseModel):
    """
    The verification agent's envelope. `output` is None on a safe failure, like the
    diagnostic's: no verdict is not a verdict to confirm, and a placeholder would be an
    opinion nobody formed. The check's Status is the reporter's answer either way — set in
    C# — so a failed agent costs the manager a second opinion, never a decision.
    """

    agent: str
    status: AgentStatus
    output: VerificationOutput | None = None
    error: str | None = None
    tool_calls: list[ToolCallOutcome] = Field(default_factory=list)


class ToolCallOutcome(BaseModel):
    """
    The result of one call to the API's tool router.

    `found` mirrors the API's own distinction: the tool ran and there was no such row
    (found=False) is not the same as the tool being refused (error set).

    `result` is a dict for the single-row tools (get_room, get_asset, get_work_order) and a LIST for
    get_asset_service_history, get_related_open_reports and get_open_work_orders. An empty
    list with found=True is an answer — "never serviced", "nothing open" — and is not the
    same as found=False, which means the asset itself was not there.
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

    # True when verification reopened the workflow: a repair did not hold, and the fault is
    # diagnosed AGAIN. It routes the run straight to the diagnostic, like the answers do —
    # re-clarifying a fault somebody already repaired would put the same questions to the
    # reporter again. It is NOT shown to any agent (DiagnosticInput has no field for it):
    # what changed since the first diagnosis — the repair's own service record, the reports
    # filed since — is in the data the tools return, read fresh on this run.
    reopened: bool = False

    # The manager's note when a proposal was sent back for revision; null on every first
    # run. Typed by a manager, and still treated as data by the strategist's prompt.
    revision_note: str | None = Field(default=None, min_length=1, max_length=MAX_REVISION_NOTE)

    # Set only when the API is asking whether a completed repair held. Its presence sends
    # the run to the verification agent and NOTHING else — see graph.py. `description` is
    # then the original report's, and the report pipeline does not run: re-clarifying a
    # fault somebody already repaired would ask the reporter questions about it again.
    verification: VerificationRequest | None = None


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

    # The strategist's proposal, when the graph ran it. An addition beside the diagnosis
    # for the same reason. A proposal and nothing more: the API decides what is raised.
    strategy: StrategistResult | None = None

    # The verification agent's verdict, on a verification run — the only field that run
    # fills. Its opinion, stored as VerificationCheck.AgentOutcome; the check's status is
    # the reporter's answer, set in C#.
    verification: VerificationResult | None = None
