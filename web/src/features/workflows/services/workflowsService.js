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
