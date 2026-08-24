using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>Response DTO. Carries the access token and just enough user detail
/// for a client to render a header without a second round trip.</summary>
public record AuthResponse(
    string Token,
    int UserId,
    string Email,
    string FullName,
    Role Role);
