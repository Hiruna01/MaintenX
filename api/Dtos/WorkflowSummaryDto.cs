using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record WorkflowSummaryDto(
    int Id,
    int? ReportId,
    string Objective,
    WorkflowState CurrentState,
    string? Outcome,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
