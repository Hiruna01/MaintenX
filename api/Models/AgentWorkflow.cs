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
    /// The maintenance report this workflow was raised for. Nullable for now — reports
    /// are a later feature, and a workflow can be started from a bare objective until
    /// then. Becomes a real foreign key when the Report entity lands.
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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<AgentStep> Steps { get; set; } = new List<AgentStep>();
}
