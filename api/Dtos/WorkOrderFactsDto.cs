using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What `get_work_order` returns: one work order as the facts of what was done, for the
/// verification agent to weigh against what happened afterwards.
///
/// FACTS ONLY. What the order claims (the resolution note), how it was approached
/// (strategy), what it cost and when it finished. Nothing here says whether the repair
/// held — that is the question the agent is asked, and a tool that answered it would be
/// handing the model its own opinion back wearing the API's authority.
///
/// Not WorkOrderDto: that carries no resolution note, which is the one field this tool
/// exists for, and it carries the technician's id and name, which nothing about whether a
/// repair held turns on.
/// </summary>
public record WorkOrderFactsDto(
    int Id,
    int ReportId,
    int AssetId,
    string AssetTag,
    WorkOrderStatus Status,
    WorkOrderStrategy Strategy,
    decimal EstimatedCost,
    decimal? ActualCost,

    // The technician's own account of the work, verbatim — "temporary fix" in here is
    // exactly the kind of admission the agent has to weigh. Null until completion.
    string? ResolutionNote,

    DateTime? CompletedAt);
