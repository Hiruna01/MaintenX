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
    DateTime UpdatedAt,

    // LLM attempts behind an agent-level step: 1, or 2 when the one retry was needed. Null on
    // a tool-call row and on a step whose agent did not report it. See AgentStep.Attempts.
    int? Attempts = null);
