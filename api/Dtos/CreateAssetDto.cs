using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO. No Id — the server assigns it.
///
/// No Status either: a newly registered asset is Active, and letting a caller register one
/// as Retired would put equipment into the estate that nothing ever serviced. Moving an
/// asset out of service is an update, not a creation — see UpdateAssetDto.
/// </summary>
public record CreateAssetDto(
    [Required]
    [MaxLength(50)]
    string AssetTag,

    [Required]
    [MaxLength(200)]
    string Name,

    [Range(1, int.MaxValue)] int AssetCategoryId,

    [Range(1, int.MaxValue)] int RoomId,

    [MaxLength(100)] string? Manufacturer,

    [MaxLength(100)] string? Model,

    DateOnly InstalledOn,

    DateOnly? WarrantyExpiresOn);
