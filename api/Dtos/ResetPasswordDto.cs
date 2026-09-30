using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO for POST /api/users/{id}/password — an Admin setting a temporary password for
/// someone who has lost theirs. There is no email flow in this system, so this is the only
/// way back in. Same length rules as registration.
/// </summary>
public record ResetPasswordDto(
    [Required][MinLength(8)][MaxLength(128)] string Password);
