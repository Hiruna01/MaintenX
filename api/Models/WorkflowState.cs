namespace CampusFacilities.Api.Models;

/// <summary>
/// Where an <see cref="AgentWorkflow"/> currently sits in the maintenance lifecycle.
///
/// Persisted as a string in PostgreSQL (see AppDbContext) and serialised by name over
/// JSON, so a stored row reads "AwaitingManagerApproval" rather than "5" and no client
/// ever hardcodes an ordinal that would shift if a member were inserted in the middle.
///
/// The transitions between these states are deterministic business rules and belong in
/// C# (WorkflowService), never in a model prompt.
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
    Reopened,
    Failed
}
