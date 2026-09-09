"""
Loader for the prompt text files in this package.

Prompts live in .md files here, never as string literals in agent code. Two reasons:
a non-programmer on the team can edit the wording without opening a Python file, and a
prompt change shows up in a diff as a prose change rather than buried in logic.

Substitution uses string.Template ($name), not str.format, because prompt files contain
example JSON full of { and } that str.format would try to interpret as fields.
"""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from string import Template

_PROMPT_DIR = Path(__file__).parent.resolve()


@lru_cache(maxsize=None)
def load_prompt(name: str) -> str:
    """Reads a prompt file from this directory. Cached — prompts do not change at runtime."""
    path = (_PROMPT_DIR / name).resolve()

    # A prompt name is a filename, never a path. Refuse anything that escapes.
    if path.parent != _PROMPT_DIR:
        raise ValueError(f"Prompt name must be a plain filename, got {name!r}")
    if not path.is_file():
        raise FileNotFoundError(f"No prompt file named {name!r} in {_PROMPT_DIR}")

    return path.read_text(encoding="utf-8").strip()


def render_prompt(name: str, **values: object) -> str:
    """Loads a prompt and substitutes $placeholders with the given values."""
    return Template(load_prompt(name)).safe_substitute(**values)
