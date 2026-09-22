using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO for creating and editing an asset category. No Id — the server assigns it on
/// a create, and it comes from the route on an update.
///
/// One DTO for both, the same way CreateRoomDto serves both: a category is its name and
/// its default warranty and nothing else, so there is no field here that may be set once
/// and never changed again — unlike Asset.AssetTag, which is why assets need two.
/// </summary>
public record CreateAssetCategoryDto(
    [Required]
    [MaxLength(100)]
    string Name,

    // Months of warranty equipment in this category normally carries. Zero is legitimate —
    // it means "none by default" — so the range starts there rather than at one. The
    // ceiling is deliberate: an extra digit typed here would otherwise become a warranty
    // date decades out on every asset created from the category afterwards.
    [Range(0, 600)]
    int DefaultWarrantyMonths);
