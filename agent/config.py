"""
Configuration for the agent service, read from the environment.

Note what is NOT here: there is no database URL, no Supabase key, no connection string
of any kind. That is the whole point of this service — it has no database credentials
and cannot grow any by accident, because there is no field here to put one in. Campus
data is read only through tools.py, which calls the API's allow-listed tool router.

`extra="ignore"` matters for the same reason: the shared root .env contains DATABASE_URL
and SUPABASE_SERVICE_KEY for the other services, and this process reads the file but
deliberately loads none of it.
"""

from __future__ import annotations

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    # The service can be started from agent/ or from the repo root, so look for both.
    model_config = SettingsConfigDict(
        env_file=(".env", "../.env"),
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore",
    )

    # --- The API, and the one credential used to talk to it -------------------
    # Base URL of the ASP.NET Core API. Default matches the "http" launch profile.
    api_base_url: str = "http://localhost:5138"

    # Sent as the X-Agent-Secret header on every internal tool call. Empty means the
    # API will reject us with 401 — it fails closed on both sides.
    agent_shared_secret: str = ""

    # --- LLM provider ---------------------------------------------------------
    # OpenAI-compatible chat-completions endpoint. Swapping OpenRouter for a local
    # Ollama server is a change to these three values and nothing else.
    llm_base_url: str = ""
    llm_api_key: str = ""
    llm_model: str = ""

    # --- Timeouts. Every outbound call gets one; nothing may hang. ------------
    llm_timeout_seconds: float = 30.0
    tool_timeout_seconds: float = 10.0

    # --- Testing --------------------------------------------------------------
    # When true the LLM client returns fixed valid JSON and the tool client returns
    # fixed context, both without touching the network. Tests run in this mode so a
    # test run can never reach a paid provider or a running API.
    stub_mode: bool = False


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    """Cached so the environment is read once per process."""
    return Settings()
