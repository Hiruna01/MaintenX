using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for the Admin's user management endpoints. <see cref="UserDto"/> plus what an
/// Admin needs to manage an account: whether it is active and when it was made.
/// <see cref="LiveWorkOrderCount"/> is the number of unfinished orders assigned to the user —
/// what blocks deactivating a Technician or moving them to another role — so the page can
/// say why before the Admin tries. Never a password hash.
/// </summary>
public record UserAdminDto(
    int Id,
    string Email,
    string FullName,
    Role Role,
    bool IsActive,
    int LiveWorkOrderCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
