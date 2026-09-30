using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IAuthService
{
    /// <summary>
    /// Creates an account. <paramref name="callerRole"/> is the role on the CALLER's token, or
    /// null for an anonymous caller — it decides which roles the request may ask for:
    ///
    ///   * anyone, signed in or not, may create a Reporter — self-registration from the phone;
    ///   * only an Admin may create any other role.
    ///
    /// The role in the request body is what is ASKED for; the caller's token is what decides
    /// whether it is granted. A body that could grant its own role would let anyone mint an
    /// Admin, which is exactly what this rule exists to stop.
    /// </summary>
    Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        Role? callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the email is unknown or the password is wrong —
    /// one indistinguishable failure, so the response cannot be used to enumerate accounts.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the token's user no longer exists.</summary>
    Task<UserDto?> GetByIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a signed, unexpired token still speaks for its user: the account exists, is
    /// active, and still has the role the token claims. Called on EVERY authenticated request
    /// (JwtBearerEvents.OnTokenValidated in Program.cs), so deactivating an account or
    /// changing its role takes effect on that person's next request rather than when their
    /// 12-hour token runs out. One primary-key lookup per request is the price; there are no
    /// refresh tokens to revoke instead.
    /// </summary>
    Task<bool> IsSessionValidAsync(int userId, string? roleClaim, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="IAuthService.RegisterAsync"/> did, so the controller can pick a status code.</summary>
public enum RegisterOutcome
{
    /// <summary>The account exists. 201.</summary>
    Registered,

    /// <summary>An account with that email already exists. 409.</summary>
    EmailTaken,

    /// <summary>The caller asked for a role only an Admin may grant. 403.</summary>
    RoleNotAllowed
}

public record RegisterResult(RegisterOutcome Outcome, AuthResponse? Response = null);
