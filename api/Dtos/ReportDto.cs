using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO. Entities are never returned from a controller directly.
///
/// The shape POST /api/reports returns: the report itself, ids only. ReportDetailDto is
/// the one that carries the resolved room, asset and clarification questions.
/// </summary>
public record ReportDto(
    int Id,
    int ReporterId,
    int RoomId,

    // Null when the reporter did not know which piece of equipment was at fault, which is
    // the normal case rather than the exception. See Report.AssetId.
    int? AssetId,

    string Description,
    ReportStatus Status,

    // Null until a photo is attached; see Report.PhotoUrl.
    string? PhotoUrl,

    DateTime CreatedAt,
    DateTime UpdatedAt);
