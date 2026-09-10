using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record ReportDto(
    int Id,
    int ReporterId,
    int RoomId,
    string Description,
    ReportStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);
