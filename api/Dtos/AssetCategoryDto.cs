namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Entities are never returned from a controller directly.</summary>
public record AssetCategoryDto(
    int Id,
    string Name,
    int DefaultWarrantyMonths,
    DateTime CreatedAt,
    DateTime UpdatedAt);
