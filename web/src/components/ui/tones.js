/**
 * Which colour each status or outcome NAME is painted in. Presentation only — a tone never
 * decides anything; it is keyed by the exact enum name the API sends, never an ordinal.
 * An unknown name falls back to slate rather than guessing a meaning.
 */
const TONES = {
  // AssetStatus
  Active: 'green',
  UnderMaintenance: 'amber',
  Retired: 'slate',

  // ServiceOutcome
  Resolved: 'green',
  TemporaryFix: 'amber',
  PartReplaced: 'violet',
  NoFaultFound: 'slate',

  // ReportStatus
  Submitted: 'blue',
  AwaitingClarification: 'amber',
  Clarified: 'violet',
  Diagnosed: 'violet',
  WorkOrderRaised: 'blue',
  Closed: 'slate',

  // ReportStage — the reporter's view. AwaitingApproval and Closed share their tone above.
  BeingReviewed: 'blue',
  WaitingOnYou: 'amber',
  RepairPlanned: 'blue',
  Repaired: 'green',
  NotGoingAhead: 'slate',

  // WorkOrderStatus
  Draft: 'slate',
  AwaitingApproval: 'amber',
  Approved: 'blue',
  Rejected: 'red',
  Scheduled: 'violet',
  InProgress: 'blue',
  Completed: 'green',
  Cancelled: 'slate',

  // VerificationStatus
  Pending: 'slate',
  AwaitingReporterResponse: 'amber',
  Confirmed: 'green',
  Reopened: 'red',
  Escalated: 'red',
  Expired: 'slate',

  // WorkflowState (the names it shares with the enums above keep their tone)
  Diagnosing: 'violet',
  Strategizing: 'violet',
  AwaitingManagerApproval: 'amber',
  AwaitingVerification: 'amber',
  Failed: 'red',

  // SlaState — the repair SLA, as the API judged it. Overdue is red because someone still has
  // to act; Missed is amber because it is history.
  None: 'slate',
  OnTrack: 'blue',
  Overdue: 'red',
  Met: 'green',
  Missed: 'amber',

  // Role — which account is which at a glance on the user list. A tone decides nothing; the
  // API's policies decide what each role may do.
  Reporter: 'slate',
  Technician: 'amber',
  FacilitiesManager: 'blue',
  Admin: 'violet',
};

export function toneFor(name) {
  return TONES[name] ?? 'slate';
}

/** "UnderMaintenance" → "Under maintenance". Sentence case reads calmer in a pill. */
export function humanize(name) {
  const spaced = String(name ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}
