/**
 * Reads the AgentStep audit trail for display. Pure functions — no fetching, no React — so
 * the reasoning panel stays presentational and every interpretation lives in one place.
 *
 * Two kinds of row arrive and they are written by two different owners:
 *   - an AGENT RUN, written by WorkflowRunner — one each for the planner, the clarifier, the
 *     diagnostic and the strategist, which run inside the same /run call. ToolCallsJson is
 *     "[]" and PayloadJson is that agent's output verbatim (`{ steps, rationale }`,
 *     `{ questions }`, `{ hypotheses, … }`, `{ strategy, estimated_cost, … }`).
 *   - a TOOL CALL, one per call, written by InternalToolsController — including calls it
 *     refuses. ToolCallsJson names the tool; PayloadJson is `{ Tool, Found, Result }`.
 *   - an APPROVAL step, written by WorkOrderService (ApprovalAudit) when an order is raised
 *     and when a manager decides. AgentName "approval", ToolCallsJson null, PayloadJson
 *     `{ workOrderId, decision, estimatedCost, basis, decidedByUserId, reason, note }`.
 *
 * Nothing here judges what an agent produced. It says what happened, in words, and the
 * raw record is always one click away.
 */

import { describeApprovalBasis, formatMoney } from '../../workorders/services/workOrdersApi';

/**
 * The ValidationResult strings the API writes. They are strings on the row, not a C# enum,
 * so they are listed here by exactly the value the API uses:
 *   Ok, NotFound, RejectedUnknownTool — InternalToolsController
 *   Ok, SafeFailure, CallFailed       — WorkflowRunner
 *   Rejected                          — WorkflowRunner, on the planner's step, when the API's
 *                                       PlanRules refused the plan and ran the fallback instead
 *   ApprovalRequired, AutoApproved,   — WorkOrderService, on an approval step (see
 *   ManagerApproved, ManagerRejected,   APPROVAL_DECISIONS below)
 *   RevisionRequested
 */
export const VALIDATION_RESULTS = {
  Ok: 'Ok',
  NotFound: 'NotFound',
  RejectedUnknownTool: 'RejectedUnknownTool',
  SafeFailure: 'SafeFailure',
  CallFailed: 'CallFailed',
  Rejected: 'Rejected',
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
  [VALIDATION_RESULTS.Rejected]: { tone: 'failed', label: 'Plan rejected', failed: true },
};

/**
 * The tone and label for an agent run's ValidationResult — the same words the audit trail
 * uses, for a page that lists runs without their payloads. An unknown tag is neutral, by name.
 */
export function validationOutcome(result) {
  return OUTCOMES[result] ?? { tone: 'neutral', label: result ?? 'Not recorded', failed: false };
}

/** The AgentName the API records the approval gate and a manager's decisions under. */
export const APPROVAL_STEP_NAME = 'approval';

/**
 * The AgentName the API records the VerificationAgent's run under
 * (`AgentRunResponse.VerificationAgentName`), on the report's workflow beside the repair.
 */
export const VERIFICATION_STEP_NAME = 'verification';

/**
 * The approval steps' outcomes. None of them is a failure: a manager saying no, or sending an
 * order back, is the control working, and painting it red would say the system broke.
 */
const APPROVAL_DECISIONS = {
  ApprovalRequired: { tone: 'waiting', label: 'Needs approval', failed: false },
  AutoApproved: { tone: 'ok', label: 'Auto-approved', failed: false },
  ManagerApproved: { tone: 'ok', label: 'Approved', failed: false },
  ManagerRejected: { tone: 'neutral', label: 'Rejected by manager', failed: false },
  RevisionRequested: { tone: 'waiting', label: 'Revision requested', failed: false },
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
 * Each agent reports its own time now. A step from an older run can still carry 0 ms — the
 * agent service then reported no split, and the whole call's time sat on the first agent —
 * so 0 is shown as such rather than as "0 ms", which would read as a measurement.
 */
export function durationLabel(step, kind) {
  if (kind === 'approval') return null;
  if (kind === 'agent' && step.durationMs === 0) return 'timed with the run';
  return `${step.durationMs.toLocaleString()} ms`;
}

/**
 * "1 attempt" / "2 attempts — retried once" for an agent run whose LLM attempts were
 * recorded; null for a tool call and for a step that predates the field. Display only.
 */
export function attemptsLabel(step) {
  if (typeof step.attempts !== 'number' || step.attempts <= 0) return null;
  if (step.attempts === 1) return '1 attempt';
  return `${step.attempts} attempts — retried ${step.attempts - 1 === 1 ? 'once' : `${step.attempts - 1} times`}`;
}

/**
 * "1,180 in · 60 out tokens" for an agent run whose provider reported usage; null for a tool
 * call, an approval step, and a run that reported none (stub mode, a failed call, a step from
 * before the columns). Null is not zero, so nothing is shown rather than "0 tokens". The two
 * counts are the API's, formatted; nothing is added up or costed here.
 */
export function tokensLabel(step) {
  if (typeof step.promptTokens !== 'number' || typeof step.completionTokens !== 'number') return null;
  return `${step.promptTokens.toLocaleString()} in · ${step.completionTokens.toLocaleString()} out tokens`;
}

/** The agents a plan payload delegates to, in order, or null when the payload is not a plan. */
export function planAgents(payload) {
  const steps = field(payload, 'steps');
  if (!Array.isArray(steps)) return null;
  return steps.map((step) => field(step, 'agent')).filter((agent) => typeof agent === 'string');
}

/** "Rs 45,000" — display only; the figure is the agent's, and nothing here compares it. */
function rupees(value) {
  const amount = Number(value);
  return Number.isNaN(amount) ? String(value) : `Rs ${amount.toLocaleString('en-LK')}`;
}

/** One sentence for a planner, diagnostic or strategist payload, or null for anything else. */
function describeAdvice(payload) {
  const agents = planAgents(payload);
  if (agents && agents.length > 0) {
    const route = agents.join(' → ');
    return agents.includes('clarifier')
      ? `Planned ${agents.length} steps: ${route}.`
      : `Planned ${agents.length} steps: ${route} — the report needs no questions.`;
  }

  const hypotheses = field(payload, 'hypotheses');
  if (Array.isArray(hypotheses) && hypotheses.length > 0) {
    const primary = hypotheses[field(payload, 'primary_hypothesis_index')] ?? hypotheses[0];
    const count = hypotheses.length === 1 ? '1 possible cause' : `${hypotheses.length} possible causes`;
    return `Diagnosed ${count}; most likely: ${field(primary, 'cause') ?? 'not named'}.`;
  }

  // The VerificationAgent's verdict on a completed repair. Its label is advice: the check's
  // status is the reporter's answer, set by the API.
  const verdict = field(payload, 'outcome');
  if (typeof verdict === 'string') {
    const confidence = field(payload, 'confidence');
    const how = typeof confidence === 'string' ? ` (${confidence} confidence)` : '';
    return `Judged the repair: ${verdict}${how} — advice; the check's status is the reporter's answer.`;
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
 * One sentence for an approval step. Which side of the threshold the order sat on is the
 * API's booleans, read through the approval queue's own `describeApprovalBasis` — nothing
 * here compares the estimate with the threshold.
 */
function describeApproval(decision, payload) {
  const order = `work order #${field(payload, 'workOrderId') ?? '?'}`;
  const basis = field(payload, 'basis');
  const cost = field(payload, 'estimatedCost');
  const reason = field(payload, 'reason');
  const note = field(payload, 'note');

  // On the gate's own steps the note says who put the order through it — the workflow runner
  // from a proposal, or a manager resubmitting a revision — as the API wrote it.
  const by = note ? ` ${note}` : '';

  switch (decision) {
    case 'ApprovalRequired':
      return basis
        ? `Raised ${order}: ${describeApprovalBasis(cost, basis).headline} — paused for a facilities manager's decision.${by}`
        : `Raised ${order} — paused for a facilities manager's decision.${by}`;
    case 'AutoApproved':
      return cost === undefined
        ? `Raised ${order} — approved by the API's threshold; nobody had to decide.${by}`
        : `Raised ${order} at ${formatMoney(cost)} — within the threshold, so approved without a decision.${by}`;
    case 'ManagerApproved':
      return `A facilities manager approved ${order}.`;
    case 'ManagerRejected':
      return reason ? `A facilities manager rejected ${order}: ${reason}` : `A facilities manager rejected ${order}.`;
    case 'RevisionRequested':
      return note
        ? `A facilities manager sent ${order} back to be re-planned: ${note}`
        : `A facilities manager sent ${order} back to be re-planned.`;
    default:
      return `Recorded an approval event on ${order}.`;
  }
}

/**
 * Everything the panel needs to render one step as a readable row:
 *   kind     — 'agent', 'tool' or 'approval'
 *   tool     — `{ tool, id }` for a tool call, null otherwise
 *   outcome  — `{ tone, label, failed }` from the ValidationResult
 *   summary  — one plain sentence saying what happened
 *   questions — the clarifier's questions as it produced them, when there are any
 */
export function describeStep(step) {
  const tool = firstToolCall(step);
  const payload = parseJson(step.payloadJson);
  const result = step.validationResult;

  // A person's decision (or the gate's routing), not an agent's output — read by name first,
  // because its ToolCallsJson is null like an agent run's would be read.
  if (step.agentName === APPROVAL_STEP_NAME) {
    const outcome = APPROVAL_DECISIONS[result] ?? { tone: 'neutral', label: result ?? 'Not recorded', failed: false };
    return { kind: 'approval', tool: null, outcome, summary: describeApproval(result, payload), questions: null };
  }

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
  } else if (result === VALIDATION_RESULTS.Rejected) {
    summary = "Proposed a plan the API's checks refused, so the run followed the default plan instead.";
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
 *
 * The planner's own step does not count as the run having happened: a plan says what WILL
 * run. Only a planner that failed or was rejected is treated as the latest outcome, because
 * then the run followed the default plan and the next steps say how that went.
 *
 * Nor does the verification agent's: it judges a finished repair, weeks after the questions,
 * and a verdict it could not give says nothing about whether the clarifier ran.
 */
export function latestAgentRunState(steps) {
  const runs = steps
    .filter((step) => step.agentName !== 'planner' && step.agentName !== VERIFICATION_STEP_NAME)
    .map(describeStep)
    .filter((step) => step.kind === 'agent');
  if (runs.length === 0) return 'none';
  return runs[runs.length - 1].outcome.failed ? 'failed' : 'ok';
}
