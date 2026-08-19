namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record BuildingDto(
    int Id,
    string Name,
    string Code,
    DateTime CreatedAt,
    DateTime UpdatedAt);
