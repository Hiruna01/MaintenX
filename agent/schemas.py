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
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, model_validator

# A clarifier round asks at most two questions. Enforced by the schema, so an
# over-eager model reply fails validation instead of reaching a user.
MAX_QUESTIONS = 2

# Longest free-text answer a reporter may give to a short_text question.
MAX_SHORT_TEXT_ANSWER = 100

# Bounds on a single_select question, so the UI always has a sane radio group.
MIN_SELECT_OPTIONS = 2
MAX_SELECT_OPTIONS = 5


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

    question_text: str = Field(min_length=1, max_length=300)
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


class ToolCallOutcome(BaseModel):
    """
    The result of one call to the API's tool router.

    `found` mirrors the API's own distinction: the tool ran and there was no such row
    (found=False) is not the same as the tool being refused (error set).
    """

    tool: str
    found: bool
    result: dict[str, Any] | None = None
    error: str | None = None


class RunRequest(BaseModel):
    """Input to POST /run. Sent by the ASP.NET Core API, never by a browser."""

    model_config = ConfigDict(extra="forbid")

    workflow_id: int
    description: str = Field(min_length=1, max_length=4000)

    # Optional context ids the agent may look up through allow-listed tools.
    room_id: int | None = None
    building_id: int | None = None


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
