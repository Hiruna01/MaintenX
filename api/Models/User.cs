using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    public Role Role { get; set; }

    // False is how a user leaves the system — the account is never deleted. Reports,
    // clarification answers and work orders all point here with Restrict foreign keys, so a
    // real delete would fail for anyone who ever did anything, and the history outlives the
    // person, the same as Asset.Status = Retired. Defaults to true in C# as well as in the
    // database, so a user created in code (the tests' bootstrap Admin) is not born disabled.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
