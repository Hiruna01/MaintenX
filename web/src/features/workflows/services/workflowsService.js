/** Every call the workflows feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';

/**
 * Mirrors the API's `WorkflowState` enum. The API binds `?state=` by NAME
 * ("AwaitingManagerApproval"), which is also what the database stores, so no ordinal is
 * ever hardcoded here.
 */
export const WORKFLOW_STATES = [
  'Submitted',
  'AwaitingClarification',
  'Diagnosing',
  'Strategizing',
  'AwaitingManagerApproval',
  'WorkOrderRaised',
  'InProgress',
  'Completed',
  'AwaitingVerification',
  'Closed',
  'Failed',
];

export const DEFAULT_PAGE_SIZE = 10;

/**
 * The status strings a plan step can carry — PlanStepStatus in the API. Strings inside
 * PlanJson, not a C# enum, so they are listed here by exactly the value the API writes.
 */
export const PLAN_STEP_STATUSES = {
  pending: 'pending',
  completed: 'completed',
  failed: 'failed',
  skipped: 'skipped',
};

const PLAN_STEP_STATUS_LABELS = {
  [PLAN_STEP_STATUSES.pending]: 'Pending',
  [PLAN_STEP_STATUSES.completed]: 'Done',
  [PLAN_STEP_STATUSES.failed]: 'No usable output',
  [PLAN_STEP_STATUSES.skipped]: 'Not run',
};

/** A plan step's status in words; an unknown one shows as sent. */
export function planStepStatusLabel(status) {
  return PLAN_STEP_STATUS_LABELS[status] ?? String(status ?? '');
}

/** Where the plan came from: the planner agent's (checked by the API), or the API's default. */
export function planSourceLabel(source) {
  return source === 'planner' ? 'From the planner' : 'Default plan';
}

/**
 * POST /api/workflows — starts a new run, 202 with the new workflow. Used to run the agents
 * again on a report whose last run ended (Failed or Closed). The API refuses with 409 while
 * the report's run is still live or the report is closed, and 403 for anyone but a
 * FacilitiesManager or an Admin; the caller shows the API's message either way.
 */
export function startWorkflow(objective, reportId) {
  return request('/api/workflows', { method: 'POST', body: { objective, reportId } });
}

/** Turns "AwaitingManagerApproval" into "Awaiting Manager Approval" for display. */
export function workflowStateLabel(state) {
  return String(state ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/**
 * Builds the path for GET /api/workflows. Returned as a string rather than fetched here,
 * because the page reads it through useFetch — which owns loading, error and the JWT.
 */
export function buildWorkflowsPath({ page = 1, pageSize = DEFAULT_PAGE_SIZE, state = '' } = {}) {
  const params = new URLSearchParams();
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));

  if (state) {
    params.set('state', state);
  }

  return `/api/workflows?${params.toString()}`;
}

/** Path for GET /api/workflows/{id} — one workflow, with its steps. */
export function buildWorkflowPath(id) {
  return `/api/workflows/${encodeURIComponent(id)}`;
}

/**
 * Parses a step's PayloadJson / ToolCallsJson.
 *
 * These arrive as JSON *strings* — they are jsonb columns the API hands back verbatim
 * rather than as nested objects, so a component that wants to read them has to parse.
 * Returns null on anything unparseable: the payload is whatever an agent produced, and a
 * malformed one must render as "not shown", never as a blank page.
 */
export function parseStepJson(raw) {
  if (!raw) return null;

  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

/**
 * The clarifying questions out of a step's payload, or an empty array.
 *
 * Note what this does NOT do: it reads questions in order to DISPLAY them. Nothing here
 * collects an answer, and there is no follow-up round — the questions are stored and
 * shown, and a human takes it from there. See the ClarifierOutput docstring in
 * agent/schemas.py; this project has no chat interface.
 */
export function stepQuestions(step) {
  const payload = parseStepJson(step?.payloadJson);
  return Array.isArray(payload?.questions) ? payload.questions : [];
}
