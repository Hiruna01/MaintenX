using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>Input DTO.</summary>
public record LoginRequest(
    [Required][EmailAddress][MaxLength(256)] string Email,
    [Required][MaxLength(128)] string Password);
