using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IAuthService
{
    /// <summary>Returns null when the email is already registered (a 409 for the caller).</summary>
    Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the email is unknown or the password is wrong —
    /// one indistinguishable failure, so the response cannot be used to enumerate accounts.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the token's user no longer exists.</summary>
    Task<UserDto?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);
}
