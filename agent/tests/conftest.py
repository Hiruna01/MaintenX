"""
Shared test setup.

Everything here exists to guarantee one thing: a test run makes no network call. STUB_MODE
is forced on before anything imports config, and the Settings objects the tests build pass
`_env_file=None` so a developer's real .env can never leak into a test.
"""

from __future__ import annotations

import os
import socket

# Set before `config` is imported anywhere. Environment variables take precedence over
# the .env file, so this wins even on a machine with a fully populated root .env.
os.environ["STUB_MODE"] = "true"
os.environ["AGENT_SHARED_SECRET"] = "test-shared-secret"
os.environ["API_BASE_URL"] = "http://api.invalid"
os.environ["LLM_BASE_URL"] = "http://llm.invalid"
os.environ["LLM_API_KEY"] = "test-key-never-used"
os.environ["LLM_MODEL"] = "stub-model"

import pytest  # noqa: E402

from config import Settings, get_settings  # noqa: E402

# The settings cache may already hold a pre-STUB_MODE read if another module imported first.
get_settings.cache_clear()


@pytest.fixture(autouse=True)
def _no_network(monkeypatch):
    """
    Blocks real sockets for the duration of every test. Applied automatically.

    This is the enforcement behind "the suite runs offline", and it is not theoretical:
    without it, a test can quietly perform DNS lookups against a placeholder host and
    still pass, because tools.py catches the failure and degrades to an empty context.
    The tests looked green while touching the network. Now that is a hard failure.
    """

    def deny(*args, **kwargs):
        raise AssertionError(
            "A test attempted real network access. The agent suite must run fully "
            "offline — use the stub settings fixture or inject a responder."
        )

    monkeypatch.setattr(socket.socket, "connect", deny)
    monkeypatch.setattr(socket.socket, "connect_ex", deny)
    monkeypatch.setattr(socket, "create_connection", deny)
    monkeypatch.setattr(socket, "getaddrinfo", deny)


@pytest.fixture
def settings() -> Settings:
    """Hermetic settings: no .env file is read at all."""
    return Settings(
        _env_file=None,
        stub_mode=True,
        api_base_url="http://api.invalid",
        agent_shared_secret="test-shared-secret",
        llm_base_url="http://llm.invalid",
        llm_api_key="test-key-never-used",
        llm_model="stub-model",
    )


@pytest.fixture
def live_settings() -> Settings:
    """
    Settings with STUB_MODE off, for tests that inject their own responder and so still
    never touch the network.
    """
    return Settings(
        _env_file=None,
        stub_mode=False,
        api_base_url="http://api.invalid",
        agent_shared_secret="test-shared-secret",
        llm_base_url="http://llm.invalid",
        llm_api_key="test-key-never-used",
        llm_model="stub-model",
    )


class ScriptedResponder:
    """
    A fake LLM. Returns the scripted replies in order, repeating the last one once the
    script runs out, and records how many times it was called and with what.

    `calls` is what the retry tests assert on: one call means no retry happened, two means
    exactly one retry, and there is no way to reach three.
    """

    def __init__(self, *replies: str) -> None:
        self.replies = list(replies)
        self.calls = 0
        self.conversations: list[list[dict[str, str]]] = []

    async def __call__(self, messages, schema) -> str:
        self.conversations.append([dict(m) for m in messages])
        reply = self.replies[min(self.calls, len(self.replies) - 1)]
        self.calls += 1
        return reply


class RaisingResponder:
    """A fake LLM that always fails the way a timeout or a dead provider would."""

    def __init__(self, exc: Exception | None = None) -> None:
        self.exc = exc or TimeoutError("provider did not respond")
        self.calls = 0

    async def __call__(self, messages, schema) -> str:
        self.calls += 1
        raise self.exc


# --- Fixed model replies used across the tests -------------------------------

VALID_TWO_QUESTIONS = """
{
  "questions": [
    {"question_text": "Is the projector showing any light?", "answer_type": "yes_no"},
    {"question_text": "How long has it been faulty?",
     "answer_type": "single_select",
     "options": ["Today", "This week", "Longer"]}
  ]
}
"""

VALID_EMPTY = '{"questions": []}'

NOT_JSON_AT_ALL = "Sure! I'd be happy to help you with that."

MALFORMED_JSON = '{"questions": [{"question_text": "Broken?", '

# Schema-invalid in three different ways, one per test that needs it.
THREE_QUESTIONS = """
{
  "questions": [
    {"question_text": "One?", "answer_type": "yes_no"},
    {"question_text": "Two?", "answer_type": "yes_no"},
    {"question_text": "Three?", "answer_type": "yes_no"}
  ]
}
"""

SELECT_WITHOUT_OPTIONS = """
{"questions": [{"question_text": "Which floor?", "answer_type": "single_select"}]}
"""

FREE_TEXT_MESSAGE_FIELD = """
{"message": "Hello! Let me know if you need anything else.", "questions": []}
"""
