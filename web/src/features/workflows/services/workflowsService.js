/** Every call the workflows feature makes to the API lives here — components never fetch. */

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
  'Reopened',
  'Failed',
];

export const DEFAULT_PAGE_SIZE = 10;

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
