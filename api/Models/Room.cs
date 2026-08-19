using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

public class Room
{
    public int Id { get; set; }

    public int BuildingId { get; set; }

    public Building? Building { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    public int Floor { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
