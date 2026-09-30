/** Every call the verification feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';

/**
 * Mirrors the API's `VerificationStatus` enum, by NAME, in the enum's own order. The API sends
 * and binds enums by name (JsonStringEnumConverter), so no ordinal is hardcoded here and a
 * member inserted into the C# enum cannot shift the client's meaning.
 */
export const VERIFICATION_STATUSES = [
  'Pending',
  'AwaitingReporterResponse',
  'Confirmed',
  'Reopened',
  'Escalated',
  'Expired',
];

/**
 * The VerificationAgent's verdicts — exactly the strings stored in
 * `VerificationCheck.AgentOutcome` and produced by the agent's `VerificationOutcome`.
 *
 * Deliberately NOT a C# enum on the API side: the outcome is the model's OPINION, stored as a
 * string so nothing reads as acting on it. The check's `status` is the reporter's answer, set in
 * C#. A value missing from this list renders as its raw text in a neutral pill, never a guess.
 */
export const AGENT_OUTCOMES = {
  confirm: 'confirm',
  reopen: 'reopen',
  escalate: 'escalate',
};

const AGENT_OUTCOME_LABELS = {
  [AGENT_OUTCOMES.confirm]: 'Confirm — the repair held',
  [AGENT_OUTCOMES.reopen]: 'Reopen — the repair did not hold',
  [AGENT_OUTCOMES.escalate]: 'Escalate — a pattern, not one failed repair',
};

export function agentOutcomeLabel(outcome) {
  return AGENT_OUTCOME_LABELS[outcome] ?? outcome ?? 'Not judged';
}

/**
 * Mirrors the API's `VerificationAgentState` enum, by NAME: where the VerificationAgent's review
 * of a check has got to. Decided in C# (`VerificationAgentRules.StateOf`) from the check's queue
 * and judgement stamps, so nothing here compares two times — the client only picks the words.
 */
export const AGENT_REVIEW_STATES = {
  NotQueued: 'NotQueued',
  Queued: 'Queued',
  Retrying: 'Retrying',
  Judged: 'Judged',
  CouldNotJudge: 'CouldNotJudge',
};

/**
 * The detail for a clarified-report count: how many of those needing no questions the PLANNER
 * judged clear, the clarifier never running on them. Words around the API's counts; no sum.
 */
export function describeNoQuestionCount(clarification, noun = 'clarified reports') {
  const base = `${clarification.reportsWithNoQuestions} of ${clarification.reportsClarified} ${noun}`;
  const planned = clarification.reportsPlannedWithoutClarification ?? 0;
  return planned > 0 ? `${base} — ${planned} judged clear by the planner` : base;
}

/**
 * True when the repair did not hold — by the reporter's answer (Reopened, Escalated) or by the
 * agent's reading (reopen, escalate). Either is a fault that has gone back round the loop, so
 * the detail page shows what it looped back to. Chooses a section to render; decides nothing.
 */
export function didNotHold(check) {
  return (
    check.status === 'Reopened' ||
    check.status === 'Escalated' ||
    check.agentOutcome === AGENT_OUTCOMES.reopen ||
    check.agentOutcome === AGENT_OUTCOMES.escalate
  );
}

/**
 * Mirrors the API's `VerificationSort` query enum. The API takes no direction: `DueAt` is latest
 * due first, and `Status` groups by the stored NAME, alphabetically.
 */
export const VERIFICATION_SORTS = {
  DueAt: 'DueAt',
  Status: 'Status',
};

export const DEFAULT_PAGE_SIZE = 10;

/** Turns "AwaitingReporterResponse" into "Awaiting Reporter Response" for display. */
export function enumLabel(value) {
  return String(value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/** A timestamp from the API (`DueAt`, `ReporterRespondedAt`) in the reader's local time. */
export function formatDateTime(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

/** "Mar 2026" for a trend point. Built from the parts the API sent, never a date string. */
export function monthLabel(year, month) {
  return new Date(year, month - 1, 1).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
}

/** A PERCENTAGE the API computed (0–100, two decimals) — formatted, never recomputed. */
export function formatPercent(value) {
  if (value === null || value === undefined) return '—';
  const amount = Number(value);
  return Number.isNaN(amount) ? '—' : `${amount.toLocaleString(undefined, { maximumFractionDigits: 2 })}%`;
}

/**
 * Builds the path for GET /api/verifications. Returned as a string rather than fetched here,
 * because the page reads it through useFetch — which owns loading, error and the JWT. Empty
 * filters are left out rather than sent blank.
 *
 * `search` matches the asset tag on the SERVER, across every page. `dateFrom` / `dateTo` are
 * the "YYYY-MM-DD" strings an <input type="date"> produces and go to the API untouched — never
 * through `new Date()`. They bound DueAt, as UTC days with both ends inclusive.
 *
 * WHO SEES WHAT IS NOT A PARAMETER. A Reporter gets the checks on their own reports and a
 * manager gets every check, decided in VerificationService from the token.
 */
export function buildVerificationsPath({
  search = '',
  status = '',
  dateFrom = '',
  dateTo = '',
  sort = VERIFICATION_SORTS.DueAt,
  page = 1,
  pageSize = DEFAULT_PAGE_SIZE,
} = {}) {
  const params = new URLSearchParams();
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  params.set('sort', sort);

  if (search.trim()) params.set('search', search.trim());
  if (status) params.set('status', status);
  if (dateFrom) params.set('dateFrom', dateFrom);
  if (dateTo) params.set('dateTo', dateTo);

  return `/api/verifications?${params.toString()}`;
}

/**
 * GET /api/verifications/{id} — the check, the work order's claim, the reporter's answer and
 * the agent's verdict. For a manager, also what has happened to the machine since.
 */
export function buildVerificationPath(id) {
  return `/api/verifications/${encodeURIComponent(id)}`;
}

/**
 * GET /api/analytics/metrics — reopen rate, clarification efficiency and repeat failures.
 * FacilitiesManager and Admin only. The range is optional; the API refuses one that ends
 * before it starts, so the page does not send it.
 */
export function buildMetricsPath({ fromDate = '', toDate = '' } = {}) {
  const params = new URLSearchParams();
  if (fromDate) params.set('fromDate', fromDate);
  if (toDate) params.set('toDate', toDate);
  const query = params.toString();
  return query ? `/api/analytics/metrics?${query}` : '/api/analytics/metrics';
}

/**
 * The reopen-rate trend as chart rows. A month in which nobody answered a check gets a `rate`
 * of null, so the line BREAKS there instead of dropping to 0% — the API sends 0 for "nothing
 * to divide by", and plotted as a point that would read as a month in which every repair held.
 * Nothing is recomputed: `answered` and `reopenRate` are the API's.
 */
export function trendChartRows(monthlyTrend) {
  return (monthlyTrend ?? []).map((month) => ({
    label: monthLabel(month.year, month.month),
    answered: month.answered,
    reopened: month.reopened,
    rate: month.answered > 0 ? Number(month.reopenRate) : null,
  }));
}

/**
 * GET /api/analytics/verification — the verification loop's own counts and rates
 * (VerificationMetricsDto). FacilitiesManager only, so only a manager's page asks for it.
 */
export const VERIFICATION_SUMMARY_PATH = '/api/analytics/verification';

/**
 * POST /api/workflows/verification-sweep — runs the same pass the API's timer runs, now.
 * FacilitiesManager only (an Admin is refused). 200 with VerificationSweepResultDto: how many
 * rows it touched and what it did with them. Safe to press twice — a second pass finds
 * nothing left to move.
 */
export function runVerificationSweep() {
  return request('/api/workflows/verification-sweep', { method: 'POST' });
}

function plural(count, one, many) {
  return `${count} ${count === 1 ? one : many}`;
}

/**
 * The sweep's counts in one sentence. Every number is the API's; nothing is added up here
 * except to say "nothing was due" when all of them are zero.
 */
export function describeSweepResult(result) {
  const parts = [];
  if (result.workflowsAwaitingVerification > 0) {
    parts.push(`${plural(result.workflowsAwaitingVerification, 'repair', 'repairs')} now awaiting verification`);
  }
  if (result.askedReporter > 0) {
    parts.push(`${plural(result.askedReporter, 'reporter', 'reporters')} asked whether the fix held`);
  }
  if (result.queuedForAgent > 0) {
    parts.push(`${plural(result.queuedForAgent, 'check', 'checks')} queued for review`);
  }

  const headline = parts.length === 0 ? 'Sweep finished — nothing was due.' : `Sweep finished: ${parts.join(', ')}.`;
  return result.failed > 0
    ? `${headline} ${plural(result.failed, 'row', 'rows')} could not be processed and will be retried on the next pass.`
    : headline;
}
