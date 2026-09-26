using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// One end-to-end run of the agent pipeline for a single maintenance objective.
/// The row is created the moment the request arrives; the work itself happens in the
/// background and updates this row, which is what clients poll.
/// </summary>
public class AgentWorkflow
{
    public int Id { get; set; }

    /// <summary>
    /// The maintenance report this workflow was raised for — a real foreign key now that
    /// Report exists (see AppDbContext).
    ///
    /// Still nullable, because the two ways a workflow is created differ: one raised by
    /// ReportService always carries a report id, while POST /api/workflows may still
    /// start one from a bare objective. Null means "no report", not "a report that got
    /// lost" — a non-null value is guaranteed by the database to name a real row.
    /// </summary>
    public int? ReportId { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Objective { get; set; } = string.Empty;

    public WorkflowState CurrentState { get; set; } = WorkflowState.Submitted;

    /// <summary>The plan the orchestrator produced. PostgreSQL jsonb, not text.</summary>
    public string? PlanJson { get; set; }

    /// <summary>Human-readable result once the workflow reaches a terminal state.</summary>
    [MaxLength(2000)]
    public string? Outcome { get; set; }

    /// <summary>Set when the background runner picks the workflow up, not at creation.</summary>
    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The completed work order whose repair did not hold, set when verification reopens the
    /// workflow (RepairReopened, AwaitingVerification -> Diagnosing). Null on a workflow that
    /// has never been reopened, and the latest one after a second reopen.
    ///
    /// It is what tells the runner a Diagnosing workflow is a RE-DIAGNOSIS rather than a
    /// resume after clarification — both start from the same state — and it names the
    /// equipment when the report never did: a work order always carries its asset, so the
    /// diagnostic can read the history the failed repair was appended to.
    /// </summary>
    public int? ReopenedWorkOrderId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
}
