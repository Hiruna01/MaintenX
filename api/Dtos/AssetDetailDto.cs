using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One asset with everything a reader needs about it: its category, where it is, and its
/// full service history oldest-first.
///
/// The history is ordered oldest-first on purpose — the opposite of the workflow list.
/// This is read to follow what happened to a machine over time, and a repeat failure only
/// reads as one when the visits are in the order they occurred.
/// </summary>
public record AssetDetailDto(
    int Id,
    string AssetTag,
    string Name,
    AssetCategoryDto Category,
    RoomDto Room,
    string? Manufacturer,
    string? Model,
    DateOnly InstalledOn,
    DateOnly? WarrantyExpiresOn,
    AssetStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ServiceRecordDto> ServiceHistory);
