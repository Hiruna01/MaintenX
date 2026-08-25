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
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AgentStepDto> Steps);
