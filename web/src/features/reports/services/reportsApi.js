/** Every call the reports feature makes to the API lives here — components never fetch. */

/**
 * Mirrors the API's `ReportStatus` enum, by NAME. The API sends and binds enums by name
 * (JsonStringEnumConverter), which is also what Postgres stores — so no ordinal is ever
 * hardcoded here, and a member inserted into the C# enum cannot shift the client's meaning.
 *
 * Deliberately NOT `WORKFLOW_STATES`: a workflow state describes one agent run and can end
 * in Failed; this says where the FAULT has got to.
 */
export const REPORT_STATUSES = [
  'Submitted',
  'AwaitingClarification',
  'Clarified',
  'Diagnosed',
  'WorkOrderRaised',
  'Closed',
];

/**
 * Mirrors the API's `AnswerType` enum, by NAME — and that is the whole list. Every
 * clarification comes back through a toggle, a picker over fixed options, or a capped short
 * string; none of them is a message box. There is no chat interface.
 */
export const ANSWER_TYPES = {
  YesNo: 'YesNo',
  SingleSelect: 'SingleSelect',
  ShortText: 'ShortText',
};

const ANSWER_TYPE_LABELS = {
  [ANSWER_TYPES.YesNo]: 'Yes / No',
  [ANSWER_TYPES.SingleSelect]: 'Pick one',
  [ANSWER_TYPES.ShortText]: 'Short answer',
};

export function answerTypeLabel(answerType) {
  return ANSWER_TYPE_LABELS[answerType] ?? answerType ?? 'Unknown';
}

/**
 * Mirrors the API's `ReportSort` query enum. The API takes no direction: `CreatedAt` is
 * newest first, and `Status` groups by the stored NAME, alphabetically — not by lifecycle
 * position, because the enum is stored as a string and never as an ordinal.
 */
export const REPORT_SORTS = {
  CreatedAt: 'CreatedAt',
  Status: 'Status',
};

export const DEFAULT_PAGE_SIZE = 10;

/** Turns "AwaitingClarification" into "Awaiting Clarification" for display. */
export function enumLabel(value) {
  return String(value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/** A timestamp from the API (`CreatedAt`, `AnsweredAt`) in the reader's local time. */
export function formatDateTime(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

/** "MAB101 · Lecture Hall A" — how a room reads on the detail page. */
export function roomLabel(room) {
  if (!room) return '—';
  return room.code ? `${room.code} · ${room.name}` : room.name;
}

/**
 * Builds the path for GET /api/reports. Returned as a string rather than fetched here,
 * because the page reads it through useFetch — which owns loading, error and the JWT.
 * Empty filters are left out entirely rather than sent blank.
 *
 * `dateFrom` / `dateTo` are the "YYYY-MM-DD" strings an <input type="date"> produces and
 * go to the API untouched — never through `new Date()`, which would parse them as UTC
 * midnight and could move them a day. Both ends are inclusive on the API side.
 *
 * WHO SEES WHAT IS NOT A PARAMETER. A Reporter gets their own reports and a manager gets
 * the estate, decided in ReportService from the token. Nothing here can widen it.
 */
export function buildReportsPath({
  search = '',
  status = '',
  dateFrom = '',
  dateTo = '',
  sort = REPORT_SORTS.CreatedAt,
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

  return `/api/reports?${params.toString()}`;
}

/**
 * GET /api/reports/{id} — the report with its room and asset resolved, its clarification
 * questions and answers, and every AgentStep recorded for it, oldest first.
 */
export function buildReportPath(id) {
  return `/api/reports/${encodeURIComponent(id)}`;
}
