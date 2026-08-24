using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO for GET /api/auth/me. Deliberately has no Token field —
/// that endpoint reports who the caller is, it does not issue credentials.</summary>
public record UserDto(
    int Id,
    string Email,
    string FullName,
    Role Role);
