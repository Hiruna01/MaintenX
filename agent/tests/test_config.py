"""
Tests for Settings — that a .env copied from .env.example actually loads.

.env.example documents every optional key as blank-means-default ("LLM_TIMEOUT_SECONDS=" —
"Default 30"). Before env_ignore_empty, that blank was read as the string "", which is not
a number or a boolean, and the service refused to start from a freshly copied template.
"""

from __future__ import annotations

from config import Settings

# The optional keys exactly as .env.example leaves them.
BLANK_TEMPLATE = """\
LLM_TIMEOUT_SECONDS=
TOOL_TIMEOUT_SECONDS=
STUB_MODE=
LLM_MODEL=
"""


def test_blank_values_in_a_copied_template_fall_back_to_the_defaults(tmp_path, monkeypatch):
    env_file = tmp_path / ".env"
    env_file.write_text(BLANK_TEMPLATE, encoding="utf-8")

    # conftest exports these for every test, and a real environment variable outranks the
    # file. Removed here so the file is what is being read.
    for name in ("STUB_MODE", "LLM_MODEL"):
        monkeypatch.delenv(name, raising=False)

    settings = Settings(_env_file=env_file)

    assert settings.llm_timeout_seconds == 30.0
    assert settings.tool_timeout_seconds == 10.0
    assert settings.stub_mode is False
    assert settings.llm_model == ""


def test_a_value_that_is_set_is_still_read(tmp_path, monkeypatch):
    env_file = tmp_path / ".env"
    env_file.write_text("LLM_TIMEOUT_SECONDS=45\nSTUB_MODE=true\n", encoding="utf-8")
    monkeypatch.delenv("STUB_MODE", raising=False)

    settings = Settings(_env_file=env_file)

    assert settings.llm_timeout_seconds == 45.0
    assert settings.stub_mode is True
