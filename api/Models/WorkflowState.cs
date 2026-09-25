namespace CampusFacilities.Api.Models;

/// <summary>
/// Where an <see cref="AgentWorkflow"/> currently sits in the maintenance lifecycle —
/// exactly the states of DEVELOPMENT_GUIDE.md §8, plus <see cref="Failed"/>.
///
/// Persisted as a string in PostgreSQL (see AppDbContext) and serialised by name over
/// JSON, so a stored row reads "AwaitingManagerApproval" rather than "5" and no client
/// ever hardcodes an ordinal that would shift if a member were inserted in the middle.
///
/// Which state may follow which is <see cref="Services.WorkflowTransitions"/>, and nowhere
/// else. The verification outcomes in §8 — Verified, Reopened, Escalated — are EDGES out of
/// AwaitingVerification (to Closed, Diagnosing and AwaitingManagerApproval), not states of
/// their own: a workflow is never "sitting in" Reopened, it is back in Diagnosing. That is
/// why there is no Reopened member, the same decision as ReportStatus having none.
/// </summary>
public enum WorkflowState
{
    Submitted,
    AwaitingClarification,
    Diagnosing,
    Strategizing,
    AwaitingManagerApproval,
    WorkOrderRaised,
    InProgress,
    Completed,
    AwaitingVerification,
    Closed,

    /// <summary>
    /// NOT in §8, deliberately kept. The agent service can be down, time out or return a
    /// safe failure, and the runner is a background worker with no request to surface that
    /// on — so the run has to end somewhere a poll can see, with the reason on the row.
    /// Without it a dead agent would leave a workflow parked in Submitted or Diagnosing
    /// forever, looking busy.
    /// </summary>
    Failed
}
