using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO for PUT /api/users/{id}. No Id (it is in the route), no password — that is its
/// own action, POST {id}/password, so saving a name never touches a credential — and no
/// IsActive: deactivating is DELETE and reactivating is POST {id}/reactivate, each with its
/// own checks, so an edit form cannot switch an account off as a side effect.
/// </summary>
public record UpdateUserDto(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MaxLength(200)] string FullName,
    [Required][EnumDataType(typeof(Role))] Role? Role);
