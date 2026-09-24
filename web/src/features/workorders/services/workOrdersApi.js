/** Every call the work orders feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';

/**
 * Mirrors the API's `WorkOrderStatus` enum, by NAME. The API sends and binds enums by name
 * (JsonStringEnumConverter), which is also what Postgres stores — so no ordinal is ever
 * hardcoded here, and a member inserted into the C# enum cannot shift the client's meaning.
 *
 * In the C# declaration order, which is roughly the lifecycle; the filter lists them so.
 */
export const WORK_ORDER_STATUSES = [
  'Draft',
  'AwaitingApproval',
  'Approved',
  'Rejected',
  'Scheduled',
  'InProgress',
  'Completed',
  'Cancelled',
];

/**
 * Mirrors the API's `WorkOrderStrategy` enum, by NAME. The agent's own values are the same
 * six in snake_case; the API maps them onto these names once, in C#, so the client only ever
 * sees these.
 */
export const STRATEGIES = [
  'KnownFix',
  'SingleJob',
  'ConsolidatedJob',
  'InspectFirst',
  'Defer',
  'EscalateReplacement',
];

const STRATEGY_LABELS = {
  KnownFix: 'Known fix',
  SingleJob: 'Single job',
  ConsolidatedJob: 'Consolidated job',
  InspectFirst: 'Inspect first',
  Defer: 'Defer',
  EscalateReplacement: 'Replace the equipment',
};

export function strategyLabel(strategy) {
  return STRATEGY_LABELS[strategy] ?? strategy ?? 'Not stated';
}

/**
 * The statuses in which an order has cleared approval and is not finished — the only ones
 * the API lets anyone assign, schedule or complete. Used to decide which controls to OFFER;
 * the API still refuses a move from anywhere else with a 409, and that answer is shown.
 */
export const ACTIVE_STATUSES = ['Approved', 'Scheduled', 'InProgress'];

/**
 * Mirrors the API's `WorkOrderSort` query enum. Exactly two orders and no direction:
 * `CreatedAt` is newest first, `Cost` is highest estimate first — sorted in C# as decimal.
 */
export const WORK_ORDER_SORTS = {
  CreatedAt: 'CreatedAt',
  Cost: 'Cost',
};

export const DEFAULT_PAGE_SIZE = 10;

/** One approval case carries a whole service history, so the queue pages in fives. */
export const APPROVAL_PAGE_SIZE = 5;

/** Turns "AwaitingApproval" into "Awaiting Approval" for display. */
export function enumLabel(value) {
  return String(value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/**
 * The agent's advice values — urgency, confidence, next action — arrive lowercase exactly as
 * it wrote them ("high", "replace"). Capitalised for display; the value itself is untouched.
 */
export function adviceLabel(value) {
  const text = String(value ?? '');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** A timestamp from the API (`CreatedAt`, `ApprovedAt`) in the reader's local time. */
export function formatDateTime(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

const RUPEES = new Intl.NumberFormat('en-LK', { maximumFractionDigits: 0 });
const RUPEES_AND_CENTS = new Intl.NumberFormat('en-LK', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * "Rs 45,000", or "Rs 4,999.50" when there are cents.
 *
 * DISPLAY ONLY. The API sends these as exact JSON numbers from C# `decimal`s, and this client
 * never adds, compares or rounds money to decide anything — whether an estimate needs a
 * manager is the API's `approvalBasis`, computed in C#. Formatting a figure someone else
 * computed is all that happens here.
 */
export function formatMoney(value) {
  if (value === null || value === undefined) return '—';
  const amount = Number(value);
  if (Number.isNaN(amount)) return '—';
  return `Rs ${(Number.isInteger(amount) ? RUPEES : RUPEES_AND_CENTS).format(amount)}`;
}

/**
 * The approval basis in plain words: "Rs 45,000 — above the Rs 15,000 approval threshold".
 *
 * Every clause is chosen by a boolean the API computed with the same C# function that routed
 * the order. Nothing here compares the estimate with the threshold, so the sentence cannot
 * disagree with what the gate actually did.
 */
export function describeApprovalBasis(estimatedCost, basis) {
  const cost = formatMoney(estimatedCost);
  const threshold = formatMoney(basis.threshold);

  if (basis.exceedsThreshold && basis.isReplacement) {
    return {
      headline: `${cost} — above the ${threshold} approval threshold`,
      detail: 'It also replaces equipment, which always needs a manager’s decision whatever it costs.',
    };
  }

  if (basis.exceedsThreshold) {
    return {
      headline: `${cost} — above the ${threshold} approval threshold`,
      detail: 'Estimates above the threshold need a manager’s decision before any work is booked.',
    };
  }

  if (basis.isReplacement) {
    return {
      headline: `${cost} — within the ${threshold} approval threshold`,
      detail: 'It replaces equipment, and a replacement always needs a manager’s decision, however cheap.',
    };
  }

  return {
    headline: `${cost} — within the ${threshold} approval threshold`,
    detail: 'Under the threshold as it is configured now, so an order like this would not need a decision.',
  };
}

const SLOT_DAY = { weekday: 'short', day: 'numeric', month: 'short' };
const SLOT_TIME = { hour: '2-digit', minute: '2-digit' };

/**
 * "Mon 29 Sep · 09:00 – 10:00", in the reader's local time. A slot is an INSTANT — the API
 * sends it in UTC with a "Z" — so `new Date()` is the right parse here, unlike a DateOnly.
 */
export function formatSlot(slot) {
  const starts = new Date(slot.startsAt);
  const ends = new Date(slot.endsAt);
  return `${starts.toLocaleDateString(undefined, SLOT_DAY)} · ${starts.toLocaleTimeString(undefined, SLOT_TIME)} – ${ends.toLocaleTimeString(undefined, SLOT_TIME)}`;
}

/** Today in the browser's calendar as "YYYY-MM-DD" — built from local parts, never via UTC. */
export function todayDateOnly() {
  const now = new Date();
  return toDateOnly(now.getFullYear(), now.getMonth(), now.getDate());
}

/** A "YYYY-MM-DD" plus a number of days, as "YYYY-MM-DD". */
export function addDaysToDateOnly(value, days) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value ?? ''));
  if (!match) return '';
  return toDateOnly(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + days);
}

function toDateOnly(year, monthIndex, day) {
  const date = new Date(year, monthIndex, day);
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * Builds the path for GET /api/workorders. Returned as a string rather than fetched here,
 * because the page reads it through useFetch — which owns loading, error and the JWT.
 * Empty filters are left out entirely rather than sent blank.
 *
 * WHO SEES WHAT IS NOT A PARAMETER. A Technician gets the orders assigned to them whatever
 * `technicianId` says, and a manager gets the estate — decided in WorkOrderService.
 */
export function buildWorkOrdersPath({
  search = '',
  status = '',
  technicianId = '',
  sort = WORK_ORDER_SORTS.CreatedAt,
  page = 1,
  pageSize = DEFAULT_PAGE_SIZE,
} = {}) {
  const params = new URLSearchParams();
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  params.set('sort', sort);

  if (search.trim()) params.set('search', search.trim());
  if (status) params.set('status', status);
  if (technicianId) params.set('technicianId', String(technicianId));

  return `/api/workorders?${params.toString()}`;
}

/** GET /api/workorders/{id} — the order with its asset, approver, approval basis and visits. */
export function buildWorkOrderPath(id) {
  return `/api/workorders/${encodeURIComponent(id)}`;
}

/**
 * GET /api/workorders/approvals — every order AwaitingApproval, oldest first, each with the
 * asset's history and summary and the agent's diagnosis and proposal. FacilitiesManager only.
 */
export function buildApprovalQueuePath(page = 1) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(APPROVAL_PAGE_SIZE) });
  return `/api/workorders/approvals?${params.toString()}`;
}

/** GET /api/users?role=Technician — the picker for assigning and filtering. FacilitiesManager only. */
export const TECHNICIANS_PATH = '/api/users?role=Technician';

/**
 * GET /api/workorders/slots/available. `fromDate` / `toDate` are campus-local calendar dates,
 * both inclusive, sent as the "YYYY-MM-DD" an <input type="date"> produces. With a
 * `technicianId`, only times that technician is also free come back.
 */
export function buildAvailableSlotsPath({ assetId, technicianId, durationMinutes, fromDate, toDate }) {
  const params = new URLSearchParams({
    assetId: String(assetId),
    durationMinutes: String(durationMinutes),
    fromDate,
    toDate,
  });
  if (technicianId) params.set('technicianId', String(technicianId));
  return `/api/workorders/slots/available?${params.toString()}`;
}

/** PUT /api/workorders/{id}/assign — 204. Does not book a time or change the status. */
export function assignTechnician(id, technicianId) {
  return request(`${buildWorkOrderPath(id)}/assign`, {
    method: 'PUT',
    body: { technicianId: Number(technicianId) },
  });
}

/**
 * POST /api/workorders/{id}/schedule — 201 with the booked slot. The slot is sent back
 * EXACTLY as the slot finder returned it, UTC with its "Z": the API re-checks it and refuses
 * a time with no offset. 409 means it was taken since it was offered.
 */
export function scheduleWorkOrder(id, slot) {
  return request(`${buildWorkOrderPath(id)}/schedule`, {
    method: 'POST',
    body: { startsAt: slot.startsAt, endsAt: slot.endsAt },
  });
}

/**
 * POST /api/workorders/{id}/complete — 204. Only the assigned technician may; the API checks
 * that against the token. The note is copied verbatim into the asset's service history.
 */
export function completeWorkOrder(id, values) {
  const photoUrl = values.completionPhotoUrl.trim();

  return request(`${buildWorkOrderPath(id)}/complete`, {
    method: 'POST',
    body: {
      // validate() has already held this to two decimal places. JSON.stringify writes a
      // number back as the shortest text that round-trips, so "1234.50" goes out as 1234.5
      // and the API reads that text straight into a decimal — no rounding on either side.
      actualCost: Number(values.actualCost),
      outcome: values.outcome,
      resolutionNote: values.resolutionNote,
      completionPhotoUrl: photoUrl === '' ? null : photoUrl,
    },
  });
}

/** POST /api/workorders/{id}/approve — 204. Who approved is taken from the token. */
export function approveWorkOrder(id) {
  return request(`${buildWorkOrderPath(id)}/approve`, { method: 'POST' });
}

/** POST /api/workorders/{id}/reject — 204. The reason is required. */
export function rejectWorkOrder(id, reason) {
  return request(`${buildWorkOrderPath(id)}/reject`, { method: 'POST', body: { reason: reason.trim() } });
}

/** POST /api/workorders/{id}/request-revision — 204. The order goes back to Draft with the note. */
export function requestRevision(id, note) {
  return request(`${buildWorkOrderPath(id)}/request-revision`, {
    method: 'POST',
    body: { note: note.trim() },
  });
}
