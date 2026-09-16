namespace CampusFacilities.Api.Models;

/// <summary>
/// Where a <see cref="Report"/> sits in its own lifecycle.
///
/// DELIBERATELY NOT THE SAME THING AS <see cref="WorkflowState"/>, and not a copy of it.
/// A workflow state describes one agent run — it can fail, and a failed run says nothing
/// about the fault still sitting in the room. This says where the FAULT has got to, which
/// is what a reporter and a manager actually want to know, and it survives a run that
/// never completed.
///
/// Persisted as a string in PostgreSQL (see AppDbContext), same as <see cref="Role"/> and
/// <see cref="WorkflowState"/>, so a row reads "AwaitingClarification" rather than "1".
///
/// Two are reachable today: a report is created Submitted, and moves to
/// AwaitingClarification when the clarifier persists questions against it. The rest are
/// the states the later components will move it through. Every transition is a
/// deterministic business rule and belongs in C# — never in a model prompt.
/// </summary>
public enum ReportStatus
{
    Submitted,
    AwaitingClarification,
    Clarified,
    Diagnosed,
    WorkOrderRaised,
    Closed
}
