import { WORKFLOW_STATES } from './workflowsService';

/**
 * The workflow lifecycle in DEVELOPMENT_GUIDE §8 order, for DISPLAY: where a state sits on
 * the rail. It is not the state machine — `WorkflowTransitions` in C# is, and it is the only
 * thing that moves a workflow. A position here says nothing about which stages a particular
 * run passed through (a clear report skips clarification).
 *
 * `Failed` is not on the line: a dead run ends there from wherever it was.
 */
export const LIFECYCLE = WORKFLOW_STATES.filter((state) => state !== 'Failed');

const SHORT_LABELS = {
  Submitted: 'Submitted',
  AwaitingClarification: 'Clarification',
  Diagnosing: 'Diagnosing',
  Strategizing: 'Strategizing',
  AwaitingManagerApproval: 'Approval',
  WorkOrderRaised: 'Order raised',
  InProgress: 'In progress',
  Completed: 'Completed',
  AwaitingVerification: 'Verification',
  Closed: 'Closed',
  Failed: 'Failed',
};

export function shortStateLabel(state) {
  return SHORT_LABELS[state] ?? state;
}

/** Index on the line, or -1 for Failed / an unknown state. */
export function lifecyclePosition(state) {
  return LIFECYCLE.indexOf(state);
}
