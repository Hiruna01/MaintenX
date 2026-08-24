namespace CampusFacilities.Api.Services;

/// <summary>
/// JWT signing configuration. Values come from configuration only — never a literal
/// in source. Built once in Program.cs and registered as a singleton: it holds no
/// DbContext and never changes after startup, so a singleton is safe here.
/// </summary>
public class JwtSettings
{
    public required string Secret { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    /// <summary>
    /// Access tokens live 12 hours. There are deliberately NO refresh tokens in this
    /// project: refresh flows need a persisted token store, rotation, reuse detection
    /// and revocation, which is a whole feature on its own and is not what this module
    /// is being marked on. A 12-hour access token covers a demo and a lab session, and
    /// the client simply logs in again when it expires.
    /// </summary>
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(12);
}
