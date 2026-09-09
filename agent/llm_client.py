"""
Provider-agnostic LLM client.

Speaks the OpenAI-compatible chat-completions shape, which OpenRouter, Together, vLLM,
LM Studio and Ollama all expose. Swapping provider is a change to LLM_BASE_URL,
LLM_API_KEY and LLM_MODEL — no code change.

## Why we do not use provider structured-output

Providers offer `response_format` / JSON-mode / tool-calling to force valid JSON. This
client deliberately does not use any of them:

  * they are not portable — the exact field differs per provider, and a local Ollama
    model may not support it at all, so depending on it would break the promise above;
  * "valid JSON" is not the same as "valid ClarifierOutput". We have to validate against
    the Pydantic schema regardless, so the provider feature would remove no code;
  * it hides failure. Prompting for JSON and validating it ourselves means a bad reply is
    a normal, testable code path rather than a provider-specific exception.

So: ask for JSON in the prompt, parse it, validate it, retry once with the validation
error appended, and on a second failure return a safe failure.

## Two guarantees this class makes to its callers

  * It never raises. Every network error, timeout, malformed body and validation failure
    comes back as `LlmJsonResult(ok=False)`.
  * It never hangs. Every request carries a timeout, and there are exactly two attempts.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from typing import Any, Awaitable, Callable, Sequence

import httpx
from pydantic import BaseModel, ValidationError

from config import Settings, get_settings
from prompts import render_prompt

logger = logging.getLogger(__name__)

# One initial attempt plus exactly one retry.
MAX_ATTEMPTS = 2

Message = dict[str, str]

# A responder turns a message list into the raw text the model replied with. Swapping it
# is how STUB_MODE avoids the network and how tests drive malformed replies.
Responder = Callable[[Sequence[Message], type[BaseModel]], Awaitable[str]]


@dataclass(frozen=True)
class LlmJsonResult:
    """
    Outcome of one `complete_json` call.

    `ok=False` is the structured safe failure: `data` is None and `error` says what went
    wrong. Callers turn that into their own empty-but-valid output.
    """

    ok: bool
    data: BaseModel | None
    error: str | None
    attempts: int


class LlmClient:
    def __init__(
        self,
        settings: Settings | None = None,
        responder: Responder | None = None,
    ) -> None:
        self._settings = settings or get_settings()

        if responder is not None:
            self._responder = responder
        elif self._settings.stub_mode:
            self._responder = self._stub_responder
        else:
            self._responder = self._http_responder

    async def complete_json(
        self,
        *,
        system: str,
        user: str,
        schema: type[BaseModel],
    ) -> LlmJsonResult:
        """
        Asks the model for JSON matching `schema`.

        Attempt 1 sends the prompt. If the reply will not parse or will not validate,
        attempt 2 resends the same conversation with the model's bad reply and the
        validation error appended. There is no attempt 3.
        """
        messages: list[Message] = [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ]
        last_error: str | None = None

        for attempt in range(1, MAX_ATTEMPTS + 1):
            raw: str | None = None
            try:
                raw = await self._responder(messages, schema)
            except Exception as exc:  # noqa: BLE001 — a provider may raise anything.
                # Timeouts, DNS failures, 500s from the provider, a body missing
                # "choices" — all the same to the caller: this attempt produced nothing.
                last_error = f"{type(exc).__name__}: {exc}"
                logger.warning("LLM attempt %s/%s failed: %s", attempt, MAX_ATTEMPTS, last_error)
            else:
                parsed, error = _parse_and_validate(raw, schema)
                if error is None:
                    return LlmJsonResult(ok=True, data=parsed, error=None, attempts=attempt)

                last_error = error
                logger.warning(
                    "LLM attempt %s/%s returned output that failed validation: %s",
                    attempt,
                    MAX_ATTEMPTS,
                    error,
                )

            if attempt < MAX_ATTEMPTS:
                messages = _with_retry_turn(messages, raw, last_error or "unknown error")

        logger.error("LLM produced no valid output after %s attempts: %s", MAX_ATTEMPTS, last_error)
        return LlmJsonResult(ok=False, data=None, error=last_error, attempts=MAX_ATTEMPTS)

    # ------------------------------------------------------------------
    # Responders
    # ------------------------------------------------------------------

    async def _http_responder(self, messages: Sequence[Message], schema: type[BaseModel]) -> str:
        """
        The real provider call. Note the absence of `response_format` — see module docstring.
        """
        url = f"{self._settings.llm_base_url.rstrip('/')}/chat/completions"
        payload: dict[str, Any] = {
            "model": self._settings.llm_model,
            "messages": list(messages),
            # Deterministic-ish: this is an extraction task, not a creative one.
            "temperature": 0,
        }
        headers = {"Content-Type": "application/json"}
        if self._settings.llm_api_key:
            headers["Authorization"] = f"Bearer {self._settings.llm_api_key}"

        # Timeout on every call, so a stalled provider cannot pin a request open.
        async with httpx.AsyncClient(timeout=self._settings.llm_timeout_seconds) as client:
            response = await client.post(url, json=payload, headers=headers)
            response.raise_for_status()
            body = response.json()

        return body["choices"][0]["message"]["content"]

    async def _stub_responder(self, messages: Sequence[Message], schema: type[BaseModel]) -> str:
        """
        STUB_MODE: fixed valid JSON, no network. The example lives on the schema itself
        so it is validated by the same rules as a real reply.
        """
        example = getattr(schema, "stub_example", None)
        if example is None:
            raise NotImplementedError(
                f"{schema.__name__} has no stub_example(); add one to use it in STUB_MODE."
            )
        return json.dumps(example())


# ----------------------------------------------------------------------
# Parsing helpers
# ----------------------------------------------------------------------


def _parse_and_validate(raw: str, schema: type[BaseModel]) -> tuple[BaseModel | None, str | None]:
    """Returns (model, None) on success or (None, human-readable error) on failure."""
    candidate = _extract_json_object(raw)
    if candidate is None:
        return None, "Reply contained no JSON object."

    try:
        data = json.loads(candidate)
    except json.JSONDecodeError as exc:
        return None, f"Reply was not valid JSON: {exc}"

    if not isinstance(data, dict):
        return None, f"Expected a JSON object, got {type(data).__name__}."

    try:
        return schema.model_validate(data), None
    except ValidationError as exc:
        # errors() rather than str(exc): shorter, and it names the offending field, which
        # is what actually helps the model fix itself on the retry.
        return None, "; ".join(
            f"{'.'.join(str(p) for p in err['loc']) or '(root)'}: {err['msg']}"
            for err in exc.errors()
        )


def _extract_json_object(raw: str) -> str | None:
    """
    Pulls the JSON object out of a reply, tolerating the two things models do anyway:
    wrapping it in ```json fences, and adding a sentence before or after it.
    """
    text = raw.strip()

    if text.startswith("```"):
        # Drop the opening fence (with or without a language tag) and the closing one.
        newline = text.find("\n")
        text = text[newline + 1 :] if newline != -1 else ""
        if text.rstrip().endswith("```"):
            text = text.rstrip()[: -len("```")]
        text = text.strip()

    start = text.find("{")
    end = text.rfind("}")
    if start == -1 or end == -1 or end < start:
        return None

    return text[start : end + 1]


def _with_retry_turn(
    messages: Sequence[Message],
    raw: str | None,
    error: str,
) -> list[Message]:
    """
    Builds the conversation for the retry: the original prompt, what the model actually
    said, and what was wrong with it. The correction text is a prompt file, not a literal.
    """
    retry: list[Message] = list(messages)
    if raw is not None:
        retry.append({"role": "assistant", "content": raw})
    retry.append({"role": "user", "content": render_prompt("json_retry.md", error=error)})
    return retry
