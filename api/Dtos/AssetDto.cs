using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a list of assets — ids only, no nested category, room or history.
/// AssetDetailDto is the one that carries those.
/// </summary>
public record AssetDto(
    int Id,
    string AssetTag,
    string Name,
    int AssetCategoryId,
    int RoomId,
    string? Manufacturer,
    string? Model,
    DateOnly InstalledOn,
    DateOnly? WarrantyExpiresOn,
    AssetStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);
