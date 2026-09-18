using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a list of verification checks — one row of a table, and nothing that
/// would make rendering that table cost a query per row. No nested asset and no agent
/// reasoning; VerificationDetailDto carries those.
///
/// AssetTag is denormalised onto the row for the same reason ReportListItemDto carries
/// RoomName: a list shows the sticker on the machine, not an object describing it.
/// </summary>
public record VerificationCheckDto(
    int Id,
    int WorkOrderId,
    int AssetId,
    string AssetTag,
    DateTime DueAt,
    VerificationStatus Status,

    // Null while unanswered, which is how a client knows which checks are still open —
    // and why this is a bool? here as well as on the entity.
    bool? ReporterConfirmed,

    DateTime? ReporterRespondedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
