/**
 * Reads the AgentStep audit trail for display. Pure functions — no fetching, no React — so
 * the reasoning panel stays presentational and every interpretation lives in one place.
 *
 * Two kinds of row arrive and they are written by two different owners:
 *   - an AGENT RUN, written by WorkflowRunner — one each for the clarifier, the diagnostic
 *     and the strategist, which run inside the same /run call. ToolCallsJson is "[]" and
 *     PayloadJson is that agent's output verbatim (`{ questions }`, `{ hypotheses, … }`,
 *     `{ strategy, estimated_cost, … }`).
 *   - a TOOL CALL, one per call, written by InternalToolsController — including calls it
 *     refuses. ToolCallsJson names the tool; PayloadJson is `{ Tool, Found, Result }`.
 *
 * Nothing here judges what an agent produced. It says what happened, in words, and the
 * raw record is always one click away.
 */

/**
 * The ValidationResult strings the API writes. They are strings on the row, not a C# enum,
 * so they are listed here by exactly the value the API uses:
 *   Ok, NotFound, RejectedUnknownTool — InternalToolsController
 *   Ok, SafeFailure, CallFailed       — WorkflowRunner
 */
export const VALIDATION_RESULTS = {
  Ok: 'Ok',
  NotFound: 'NotFound',
  RejectedUnknownTool: 'RejectedUnknownTool',
  SafeFailure: 'SafeFailure',
  CallFailed: 'CallFailed',
};

/**
 * NotFound is NOT a failure. A tool that looked for a record and found none has answered
 * the question it was asked — "null and empty are different answers" — and painting that
 * red would tell a reader the system broke when it did its job.
 */
const OUTCOMES = {
  [VALIDATION_RESULTS.Ok]: { tone: 'ok', label: 'Ok', failed: false },
  [VALIDATION_RESULTS.NotFound]: { tone: 'neutral', label: 'Not found', failed: false },
  [VALIDATION_RESULTS.RejectedUnknownTool]: { tone: 'failed', label: 'Rejected', failed: true },
  [VALIDATION_RESULTS.SafeFailure]: { tone: 'failed', label: 'Safe failure', failed: true },
  [VALIDATION_RESULTS.CallFailed]: { tone: 'failed', label: 'Call failed', failed: true },
};

/**
 * Parses a step's PayloadJson / ToolCallsJson. These are jsonb columns the API hands back
 * verbatim as STRINGS, not nested objects. Returns null on anything unparseable: the payload
 * is whatever an agent produced, and a malformed one must render as "not shown", never as a
 * blank page.
 */
export function parseJson(raw) {
  if (!raw) return null;

  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

/** Pretty-printed for the "show raw" view; the original string if it does not parse. */
export function prettyJson(raw) {
  const parsed = parseJson(raw);
  return parsed === null ? raw : JSON.stringify(parsed, null, 2);
}

/**
 * Reads a property whichever casing it arrived in. The tool payload is serialised by
 * System.Text.Json with default options, so it is PascalCase ("Found", "Result"), while the
 * agent's own output is snake_case. The panel should not break if either is ever changed.
 */
function field(object, name) {
  if (!object || typeof object !== 'object') return undefined;
  if (name in object) return object[name];
  const pascal = name.charAt(0).toUpperCase() + name.slice(1);
  return object[pascal];
}

/** The first tool call recorded on the step, or null for an agent run (`"[]"`). */
function firstToolCall(step) {
  const calls = parseJson(step.toolCallsJson);
  if (!Array.isArray(calls) || calls.length === 0) return null;
  const call = calls[0];
  return call?.tool ? { tool: call.tool, id: call.arguments?.id } : null;
}

/** "PRJ-MAB101-01 · Epson EB-L200F" / "MAB101 · Lecture Hall A" — the row a tool returned. */
function recordLabel(result) {
  const key = field(result, 'assetTag') ?? field(result, 'code');
  const name = field(result, 'name');
  return [key, name].filter(Boolean).join(' · ');
}

/** What a tool call returned, in words — the fact, not what it means. */
function describeToolResult(payload) {
  const result = field(payload, 'result');

  if (Array.isArray(result)) {
    // Found with an empty list is a real answer: the record exists and has nothing
    // against it. It is not the same as an unknown id, which comes back as NotFound.
    if (result.length === 0) return 'found, with nothing in it';
    return `returned ${result.length} ${result.length === 1 ? 'row' : 'rows'}`;
  }

  const label = recordLabel(result);
  return label ? `found ${label}` : 'found the record';
}

/**
 * The diagnostic's and strategist's steps are recorded with 0 ms: all three agents ran inside
 * one /run call whose time is on the clarifier's step, and the agent reports no split. Shown
 * as such rather than as "0 ms", which would read as a measurement.
 */
export function durationLabel(step, kind) {
  if (kind === 'agent' && step.durationMs === 0) return 'timed with the run';
  return `${step.durationMs.toLocaleString()} ms`;
}

/** "Rs 45,000" — display only; the figure is the agent's, and nothing here compares it. */
function rupees(value) {
  const amount = Number(value);
  return Number.isNaN(amount) ? String(value) : `Rs ${amount.toLocaleString('en-LK')}`;
}

/** One sentence for a diagnostic or strategist payload, or null for anything else. */
function describeAdvice(payload) {
  const hypotheses = field(payload, 'hypotheses');
  if (Array.isArray(hypotheses) && hypotheses.length > 0) {
    const primary = hypotheses[field(payload, 'primary_hypothesis_index')] ?? hypotheses[0];
    const count = hypotheses.length === 1 ? '1 possible cause' : `${hypotheses.length} possible causes`;
    return `Diagnosed ${count}; most likely: ${field(primary, 'cause') ?? 'not named'}.`;
  }

  const strategy = field(payload, 'strategy');
  if (typeof strategy === 'string') {
    const cost = field(payload, 'estimated_cost');
    const words = strategy.replaceAll('_', ' ');
    return cost === undefined ? `Proposed ${words}.` : `Proposed ${words} at ${rupees(cost)} — advice; approval is decided by the API.`;
  }

  return null;
}

/** The clarifier's questions out of an agent-run payload, or null if the payload has none. */
export function payloadQuestions(payload) {
  const questions = field(payload, 'questions');
  return Array.isArray(questions) ? questions : null;
}

/**
 * Everything the panel needs to render one step as a readable row:
 *   kind     — 'agent' or 'tool'
 *   tool     — `{ tool, id }` for a tool call, null otherwise
 *   outcome  — `{ tone, label, failed }` from the ValidationResult
 *   summary  — one plain sentence saying what happened
 *   questions — the clarifier's questions as it produced them, when there are any
 */
export function describeStep(step) {
  const tool = firstToolCall(step);
  const payload = parseJson(step.payloadJson);
  const result = step.validationResult;

  const outcome = OUTCOMES[result] ?? {
    tone: 'neutral',
    label: result ?? 'Not recorded',
    // A step that carries an error message failed, whatever its tag says.
    failed: Boolean(step.errorMessage),
  };

  if (tool) {
    const target = tool.id === undefined ? tool.tool : `${tool.tool} for id ${tool.id}`;
    let summary;

    if (result === VALIDATION_RESULTS.RejectedUnknownTool) {
      summary = `Asked for ${tool.tool} — refused, because it is not on the API's allow-list.`;
    } else if (result === VALIDATION_RESULTS.NotFound) {
      summary = `Called ${target} — no such record.`;
    } else if (result === VALIDATION_RESULTS.Ok) {
      summary = `Called ${target} — ${describeToolResult(payload)}.`;
    } else {
      summary = `Called ${target}.`;
    }

    return { kind: 'tool', tool, outcome, summary, questions: null };
  }

  const questions = payloadQuestions(payload);
  const advice = describeAdvice(payload);
  let summary;

  if (result === VALIDATION_RESULTS.CallFailed) {
    summary = 'The agent service could not be reached, or did not give a readable answer.';
  } else if (result === VALIDATION_RESULTS.SafeFailure) {
    summary = 'The agent ran but could not produce a valid answer, so it returned nothing.';
  } else if (questions && questions.length === 0) {
    summary = 'Asked no questions — the report already said enough to act on.';
  } else if (questions) {
    summary = `Asked the reporter ${questions.length} ${questions.length === 1 ? 'question' : 'questions'}.`;
  } else if (advice) {
    summary = advice;
  } else if (payload !== null) {
    summary = 'Finished and recorded its output.';
  } else {
    summary = 'Finished without recording any output.';
  }

  return { kind: 'agent', tool: null, outcome, summary, questions };
}

/**
 * Splits the flat, oldest-first list into consecutive runs by WorkflowId. A report is
 * clarified by one workflow in almost every case, but a second run must still be tellable
 * apart. Order is preserved exactly — nothing is re-sorted.
 */
export function groupByWorkflow(steps) {
  const groups = [];

  for (const step of steps) {
    const last = groups[groups.length - 1];
    if (last && last.workflowId === step.workflowId) {
      last.steps.push(step);
    } else {
      groups.push({ workflowId: step.workflowId, steps: [step] });
    }
  }

  return groups;
}

/**
 * How the most recent agent run on the report went: 'none' if no run has been recorded,
 * 'failed' if the latest one failed, 'ok' otherwise. Lets an empty question list say which
 * of three different things it means — not asked yet, could not ask, or asked nothing.
 */
export function latestAgentRunState(steps) {
  const runs = steps.map(describeStep).filter((step) => step.kind === 'agent');
  if (runs.length === 0) return 'none';
  return runs[runs.length - 1].outcome.failed ? 'failed' : 'ok';
}
