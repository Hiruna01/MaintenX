using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// A class of equipment — projectors, air conditioners — carrying the defaults that apply
/// to everything in it.
/// </summary>
public class AssetCategory
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Months of warranty a newly installed asset of this category normally gets. A
    /// default for data entry, not a rule: an asset's own WarrantyExpiresOn is what any
    /// warranty decision reads, because a particular unit may have been bought on
    /// different terms.
    /// </summary>
    public int DefaultWarrantyMonths { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
