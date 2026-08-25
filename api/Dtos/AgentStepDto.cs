namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record AgentStepDto(
    int Id,
    int WorkflowId,
    string AgentName,
    string? ToolCallsJson,
    int DurationMs,
    string? ValidationResult,
    string? ErrorMessage,
    string? PayloadJson,
    DateTime CreatedAt,
    DateTime UpdatedAt);
