namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record RoomDto(
    int Id,
    int BuildingId,
    string Name,
    string Code,
    int Floor,
    DateTime CreatedAt,
    DateTime UpdatedAt);
