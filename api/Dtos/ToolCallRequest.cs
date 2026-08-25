using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Body of a call from the agent service to /api/internal/tools/{toolName}.
///
/// Deliberately narrow: both allow-listed tools ("get_room", "get_building") look one
/// entity up by its id, so a single Id field is all the argument surface there is. When a
/// tool needs a richer argument shape, add a named field here — the point is that the
/// caller can never widen it for us.
/// </summary>
public record ToolCallRequest(
    [Required] int? WorkflowId,
    [Required] int? Id,
    [MaxLength(100)] string? AgentName = null);
