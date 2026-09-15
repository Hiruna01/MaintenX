using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// One piece of physical equipment in a room — the thing a report is ultimately about.
/// </summary>
public class Asset
{
    public int Id { get; set; }

    /// <summary>
    /// The label physically stuck on the equipment, and the payload encoded in its QR code
    /// — scanning a sticker yields this string and nothing else, so it is the only handle a
    /// reporter has on an asset. Unique across the estate.
    ///
    /// It is therefore not editable once assigned: changing it would leave the printed
    /// sticker pointing at nothing. See UpdateAssetDto, which deliberately omits it.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string AssetTag { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int AssetCategoryId { get; set; }

    public AssetCategory? Category { get; set; }

    public int RoomId { get; set; }

    public Room? Room { get; set; }

    [MaxLength(100)]
    public string? Manufacturer { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>
    /// DateOnly, not DateTime: an installation is a calendar date, and giving it a time
    /// component invites a timezone conversion to move it across midnight. Npgsql maps it
    /// to `date`.
    /// </summary>
    public DateOnly InstalledOn { get; set; }

    /// <summary>
    /// Null means no warranty is recorded — which is not the same as "expired". Any
    /// warranty rule reads this column, and it is a deterministic business rule, so it
    /// belongs in C# and never in a model prompt.
    /// </summary>
    public DateOnly? WarrantyExpiresOn { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<ServiceRecord> ServiceRecords { get; set; } = new List<ServiceRecord>();
}
