using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Body of a call from the agent service to /api/internal/tools/{toolName}.
///
/// Deliberately narrow: every allow-listed tool answers a question about one entity named
/// by its id, so a single Id field is all the argument surface there is. What that id
/// means is the tool's business — a room for "get_room", an ASSET for
/// "get_asset_service_history" and "get_related_open_reports".
///
/// There is no page size, no limit and no date range here, and that is the point. The row
/// caps on the list-returning tools are constants in the services; a caller that could
/// send its own limit could ask for the whole table. When a tool genuinely needs a richer
/// argument shape, add a named field here — the caller can never widen it for us.
/// </summary>
public record ToolCallRequest(
    [Required] int? WorkflowId,
    [Required] int? Id,
    [MaxLength(100)] string? AgentName = null);
