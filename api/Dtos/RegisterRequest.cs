using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO. No Id — the server assigns it.
///
/// <see cref="Role"/> is optional and defaults to Reporter, which is the only role a caller
/// without an Admin token may ask for. Who may create which role is decided in
/// AuthService.RegisterAsync from the CALLER's token, never from this body — see there.
/// </summary>
public record RegisterRequest(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MinLength(8)][MaxLength(128)] string Password,
    [Required][MaxLength(200)] string FullName,
    [EnumDataType(typeof(Role))] Role? Role = null);
