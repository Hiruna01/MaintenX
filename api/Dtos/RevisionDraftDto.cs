using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — the report's work order that a facilities manager sent back for revision,
/// as it stands in Draft. While it exists the report offers "resubmit this order" rather than
/// "raise a work order": POST /api/workorders refuses a second one beside it, and
/// POST /api/workorders/{id}/resubmit puts this one through the approval gate again.
///
/// Strategy and EstimatedCost are the order's CURRENT values — what was sent back — not the
/// strategist's revised proposal, which is ReportDetailDto.Proposal.
/// </summary>
public record RevisionDraftDto(
    int WorkOrderId,
    int AssetId,
    string AssetTag,
    WorkOrderStrategy Strategy,
    decimal EstimatedCost,
    string? PartsRequired,
    string? RevisionNote);
