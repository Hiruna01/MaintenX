using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a single workflow, including its steps in the order they happened.
/// This is what a polling client reads.
/// </summary>
public record WorkflowDetailDto(
    int Id,
    int? ReportId,
    string Objective,
    WorkflowState CurrentState,
    string? PlanJson,
    string? Outcome,
    DateTime? StartedAt,
    DateTime? CompletedAt,

    // The completed work order whose repair did not hold, when verification reopened this
    // workflow; null otherwise. See AgentWorkflow.ReopenedWorkOrderId.
    int? ReopenedWorkOrderId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AgentStepDto> Steps,

    // Every time the diagnostic ran on this workflow, oldest first, read by
    // AgentAnalysis.ToDiagnosis — the reader the approval queue uses, so null, failed and
    // unreadable mean the same here. One entry on most workflows; a second and later one each
    // come from a repair verification reopened, and sit beside the first rather than over it.
    // The steps above still carry every payload verbatim.
    IReadOnlyList<AgentDiagnosisDto> Diagnoses);
