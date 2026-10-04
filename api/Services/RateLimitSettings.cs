namespace CampusFacilities.Api.Services;

/// <summary>
/// Request budgets, enforced by ASP.NET Core's built-in rate limiter (Program.cs). From
/// configuration, never a literal: a budget changes with the size of the campus.
///
///   * AUTH — POST /api/auth/login and /register, per client address. Without it a password
///     can be guessed as fast as the API answers, and the anonymous register endpoint can
///     mint accounts in bulk. 10 a minute is far more than a person mistyping a password.
///   * REPORTS — POST /api/reports, per signed-in user. Every report starts an agent run of
///     up to eight LLM calls, so filing is the one request whose cost lands on the LLM bill.
///     10 an hour is more than any reporter files, and a budget on what a script could spend.
///
/// A refused request is a 429 with Retry-After and a ProblemDetails saying when to try
/// again, written by RateLimitRules.OnRejectedAsync. Nothing is written for it.
/// </summary>
public class RateLimitSettings
{
    /// <summary>The policy name on login and register.</summary>
    public const string AuthPolicy = "auth";

    /// <summary>The policy name on filing a report.</summary>
    public const string ReportsPolicy = "reports";

    public const int DefaultAuthAttemptsPerMinute = 10;
    public const int DefaultReportsPerHour = 10;

    /// <summary>Login and register requests per client address per minute.</summary>
    public int AuthAttemptsPerMinute { get; init; } = DefaultAuthAttemptsPerMinute;

    /// <summary>Reports filed per user per hour.</summary>
    public int ReportsPerHour { get; init; } = DefaultReportsPerHour;
}
