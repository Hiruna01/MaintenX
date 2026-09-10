namespace CampusFacilities.Api.Models;

/// <summary>
/// Where a <see cref="Report"/> sits in its own lifecycle, independent of the agent
/// workflow raised for it. Persisted as a string in PostgreSQL (see AppDbContext), same
/// as <see cref="Role"/> and <see cref="WorkflowState"/>, so a row reads "Submitted"
/// rather than "0".
///
/// Only Submitted is reachable today: a report is created and nothing moves it on yet.
/// The transitions are deterministic business rules and belong in C# (ReportService)
/// when they land — never in a model prompt.
/// </summary>
public enum ReportStatus
{
    Submitted,
    InProgress,
    Resolved,
    Closed
}
