"""
Schema tests. These pin the rules the prompt only *asks* for — the prompt is a request,
the schema is the enforcement.
"""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from schemas import (
    MAX_SHORT_TEXT_ANSWER,
    AnswerType,
    ClarifierOutput,
    ClarifyingQuestion,
)


def test_more_than_two_questions_is_rejected():
    with pytest.raises(ValidationError):
        ClarifierOutput(
            questions=[
                {"question_text": f"Q{i}?", "answer_type": "yes_no"} for i in range(3)
            ]
        )


def test_single_select_requires_options():
    with pytest.raises(ValidationError, match="single_select"):
        ClarifyingQuestion(question_text="Which floor?", answer_type="single_select")


def test_single_select_needs_at_least_two_options():
    with pytest.raises(ValidationError, match="single_select"):
        ClarifyingQuestion(
            question_text="Which floor?", answer_type="single_select", options=["Ground"]
        )


def test_yes_no_must_not_carry_options():
    with pytest.raises(ValidationError, match="must not carry options"):
        ClarifyingQuestion(
            question_text="Is it broken?", answer_type="yes_no", options=["Yes", "No"]
        )


def test_short_text_answers_are_capped_at_100_characters():
    question = ClarifyingQuestion(question_text="What is the label code?", answer_type="short_text")

    assert question.max_answer_length == MAX_SHORT_TEXT_ANSWER == 100


def test_the_cap_is_ours_not_the_models():
    """A model claiming a 5000-character answer limit does not get one."""
    with pytest.raises(ValidationError):
        ClarifyingQuestion(
            question_text="Describe it.", answer_type="short_text", max_answer_length=5000
        )


def test_a_non_short_text_question_carries_no_length_cap():
    question = ClarifyingQuestion(question_text="Is it broken?", answer_type="yes_no")

    assert question.max_answer_length is None


def test_unknown_answer_types_are_rejected():
    """No free_text, no long_text, no 'chat'."""
    with pytest.raises(ValidationError):
        ClarifyingQuestion(question_text="Tell me everything.", answer_type="free_text")

    assert {t.value for t in AnswerType} == {"yes_no", "single_select", "short_text"}


def test_extra_fields_are_rejected_not_ignored():
    with pytest.raises(ValidationError):
        ClarifierOutput(questions=[], message="Anything else I can help with?")


def test_empty_output_is_valid():
    assert ClarifierOutput().questions == []


def test_stub_example_matches_the_schema():
    output = ClarifierOutput.model_validate(ClarifierOutput.stub_example())

    assert len(output.questions) <= 2
