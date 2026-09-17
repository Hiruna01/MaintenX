using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a list of work orders — one row of a table, and nothing that would
/// make rendering that table cost a query per row. No nested asset, no scheduled slots;
/// WorkOrderDetailDto carries those.
///
/// AssetTag and AssignedTechnicianName are denormalised onto the row for the same reason
/// ReportListItemDto carries RoomName: a list shows the sticker on the machine and the
/// name of whoever is going, and neither is worth a nested object to say.
/// </summary>
public record WorkOrderDto(
    int Id,
    int ReportId,
    int AssetId,
    string AssetTag,
    int? AssignedTechnicianId,

    // Null while the order is unassigned, which is every order when it is raised.
    string? AssignedTechnicianName,

    WorkOrderStatus Status,
    WorkOrderStrategy Strategy,

    // decimal on the way out as well as in the database. A list of costs that a client
    // totals or sorts must not be handed figures that have already been through a binary
    // float — System.Text.Json writes decimal as an exact JSON number.
    decimal EstimatedCost,
    decimal? ActualCost,

    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
