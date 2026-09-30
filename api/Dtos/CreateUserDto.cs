using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO for an Admin creating an account (POST /api/users). No Id — the server assigns
/// it — and no IsActive: every account starts active.
///
/// Unlike <see cref="RegisterRequest"/>, <see cref="Role"/> is REQUIRED: an Admin creating an
/// account says which role it is, and a nullable with [Required] makes a missing role a 400
/// rather than the enum's first member (Reporter) chosen for them. The limits are
/// RegisterRequest's, so an account is held to one set of rules however it was made.
/// </summary>
public record CreateUserDto(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MaxLength(200)] string FullName,
    [Required][MinLength(8)][MaxLength(128)] string Password,
    [Required][EnumDataType(typeof(Role))] Role? Role);
