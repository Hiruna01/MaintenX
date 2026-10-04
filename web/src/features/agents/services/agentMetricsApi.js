/**
 * GET /api/analytics/agents — how the five agents themselves are doing (AgentMetricsDto).
 *
 * EVERY FIGURE IS THE API'S. Runs, rates, the median and p95, token totals and the estimated
 * cost are all counted in AgentMetricsService; this file builds the path and formats what
 * comes back. Null means "nothing to compute from" — no timed runs, no reported tokens, no
 * price — and is shown as such, never as 0.
 */

/** The agent-level step names, by NAME, in the order the API lists them. */
export const AGENT_LABELS = {
  planner: 'Planner',
  clarifier: 'Clarifier',
  diagnostic: 'Diagnostic',
  strategist: 'Resolution strategist',
  verification: 'Verification',
};

export function agentLabel(name) {
  return AGENT_LABELS[name] ?? name;
}

/**
 * The path, with the optional range as the "YYYY-MM-DD" strings a date input produces —
 * never through `new Date()`. The API refuses a range that ends before it starts, so the page
 * does not send one.
 */
export function buildAgentMetricsPath({ fromDate = '', toDate = '' } = {}) {
  const params = new URLSearchParams();
  if (fromDate) params.set('fromDate', fromDate);
  if (toDate) params.set('toDate', toDate);
  const query = params.toString();
  return query ? `/api/analytics/agents?${query}` : '/api/analytics/agents';
}

/** "12,480" — a count the API reported; "—" when there is none. */
export function formatTokens(value) {
  if (value === null || value === undefined) return '—';
  return Number(value).toLocaleString();
}

/** "850 ms" or "9.7 s" — a duration the API measured; "—" when nothing was timed. */
export function formatDurationMs(value) {
  if (value === null || value === undefined) return '—';
  if (value < 1000) return `${value.toLocaleString()} ms`;
  return `${(value / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 })} s`;
}

/**
 * "$0.002415" — the API's ESTIMATE, in US dollars to the six places it sends. One run costs
 * fractions of a cent, so two places would show almost every run as $0.00.
 */
export function formatUsd(value) {
  if (value === null || value === undefined) return '—';
  return `$${Number(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`;
}

/**
 * The cost cell's words. Three different facts, so three different answers: no price is
 * configured, nothing reported usage, or the API's estimate.
 */
export function describeCost(value, pricing, runsWithUsage) {
  if (!pricing?.configured) return 'Price not configured';
  if (!runsWithUsage) return 'No usage reported';
  return formatUsd(value);
}

/** "3 of 7 runs" beside a rate, so 0% of nothing reads differently from a real 0%. */
export function ofRuns(part, whole, noun = 'runs') {
  return `${part} of ${whole} ${noun}`;
}

/** "12 May" for a day of the series. Read from the DateOnly's parts, never `new Date(string)`. */
export function dayLabel(dateOnly) {
  const [year, month, day] = dateOnly.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
}

/**
 * The daily series as chart rows. A day whose runs reported no usage has null tokens, so its
 * bar is missing rather than drawn at 0 — the API's null means "unknown", not "free". A day
 * with no runs is the API's real 0.
 */
export function dailyChartRows(daily) {
  return (daily ?? []).map((day) => ({
    label: dayLabel(day.date),
    date: day.date,
    runs: day.runs,
    runsWithUsage: day.runsWithUsage,
    promptTokens: day.promptTokens,
    completionTokens: day.completionTokens,
    estimatedCostUsd: day.estimatedCostUsd,
  }));
}
