using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO for editing an asset. No Id — it comes from the route.
///
/// AND NO AssetTag, deliberately. The tag is printed on a sticker on the equipment and
/// encoded in its QR code; editing it here would silently orphan every sticker already
/// applied, and there would be no way to tell from the database that it had happened. A
/// mislabelled asset gets a new sticker and a new record, not a renamed row.
///
/// Status IS editable here — this is how an asset is put under maintenance or retired.
/// </summary>
public record UpdateAssetDto(
    [Required]
    [MaxLength(200)]
    string Name,

    [Range(1, int.MaxValue)] int AssetCategoryId,

    [Range(1, int.MaxValue)] int RoomId,

    [MaxLength(100)] string? Manufacturer,

    [MaxLength(100)] string? Model,

    DateOnly InstalledOn,

    DateOnly? WarrantyExpiresOn,

    [EnumDataType(typeof(AssetStatus))] AssetStatus Status);
