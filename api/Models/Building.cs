using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

public class Building
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
