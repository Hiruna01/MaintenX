namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Where a report has got to, in the words its REPORTER is shown. Derived in C# by
/// <see cref="Services.ReportProgress.StageFor"/> from the report's status, its latest
/// workflow's state and whether its latest work order was rejected — never stored, so it
/// lives beside the DTOs rather than in Models, like <see cref="ReportSort"/>.
///
/// Neither a copy of <see cref="Models.ReportStatus"/> (where the FAULT has got to) nor of
/// <see cref="Models.WorkflowState"/> (one agent run): a reporter is not told that a run
/// Failed or is Strategizing, and nothing here carries a cost, an estimate or a technician.
/// AwaitingApproval says a manager has to sign the repair off, never why.
///
/// Sent and accepted by NAME, like every other enum in this API.
/// </summary>
public enum ReportStage
{
    /// <summary>Filed and being looked at: the agents are running, or staff have it after a run failed.</summary>
    BeingReviewed,

    /// <summary>The clarification questions are waiting on the reporter.</summary>
    WaitingOnYou,

    /// <summary>A work order has been raised and is waiting on a manager's decision.</summary>
    AwaitingApproval,

    /// <summary>An approved work order: the repair is planned or under way.</summary>
    RepairPlanned,

    /// <summary>The technician has finished; the repair check follows after the delay.</summary>
    Repaired,

    /// <summary>A manager rejected the work order, so this report will not be repaired as filed.</summary>
    NotGoingAhead,

    /// <summary>Closed — the repair held, or staff closed the report (a duplicate, nothing wrong).</summary>
    Closed
}
