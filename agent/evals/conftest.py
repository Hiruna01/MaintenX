"""
Evidence capture for the live evals — records what the model actually said.

The evals assert on the model's behaviour, but a pass or fail on its own is weak evidence
for a report: a reader needs to see the reply. When `EVAL_RECORD_PATH` is set, every
`LlmClient.complete_json` call made during an eval is appended to that file as one JSON line:
the test it ran under, the schema it was validated against, ok / safe failure, how many
attempts it took, how long it took, and the validated output (or the error).

Recording only. It wraps the real method and returns its result unchanged, so it cannot
change what an eval asserts. Prompts are NOT recorded — they are in `prompts/*.md` already,
and the report data inside them is the eval's own fixture. Unset, this does nothing.

Not collected by CI: `testpaths = tests` in pytest.ini keeps `evals/` out of every normal run.
"""

import json
import os
import time
from datetime import datetime, timezone

import pytest

import llm_client


@pytest.fixture(autouse=True)
def record_llm_replies(request, monkeypatch):
    path = os.environ.get("EVAL_RECORD_PATH")
    if not path:
        yield
        return

    original = llm_client.LlmClient.complete_json

    async def recording(self, *args, **kwargs):
        started = time.perf_counter()
        result = await original(self, *args, **kwargs)
        elapsed_ms = round((time.perf_counter() - started) * 1000)

        schema = kwargs.get("schema") or next(
            (a for a in args if isinstance(a, type)), None
        )
        output = result.data.model_dump(mode="json") if result.data is not None else None

        with open(path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps({
                "recorded_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                "test": request.node.nodeid,
                "schema": getattr(schema, "__name__", None),
                "ok": result.ok,
                "attempts": result.attempts,
                "duration_ms": elapsed_ms,
                "output": output,
                "error": result.error,
            }, ensure_ascii=False) + "\n")

        return result

    monkeypatch.setattr(llm_client.LlmClient, "complete_json", recording)
    yield
