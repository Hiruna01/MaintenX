using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The two rate-limiting policies and what a refused request gets back. See RateLimitSettings
/// for the budgets and why each exists.
///
/// FIXED WINDOWS, no queue: a refused request is answered at once, never held. The budget is
/// read from RateLimitSettings when a partition is first created, through the request's
/// services, so a test can register its own settings.
/// </summary>
public static class RateLimitRules
{
    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = OnRejectedAsync;

        // Per client ADDRESS: the caller is not signed in yet, so the address is all there is.
        // Behind Render's proxy it is the X-Forwarded-For the proxy appended (UseForwardedHeaders
        // in Program.cs); without that every caller would share the proxy's one address.
        options.AddPolicy(RateLimitSettings.AuthPolicy, context =>
        {
            var settings = context.RequestServices.GetRequiredService<RateLimitSettings>();
            var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.AuthAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        });

        // Per SIGNED-IN USER, from the token's sub: one reporter's budget is never another's,
        // and changing address does not reset it. The limiter runs after authorization, so an
        // anonymous caller has already been answered 401 and never reaches it.
        options.AddPolicy(RateLimitSettings.ReportsPolicy, context =>
        {
            var settings = context.RequestServices.GetRequiredService<RateLimitSettings>();
            var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.ReportsPerHour,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            });
        });
    }

    /// <summary>
    /// 429 with Retry-After (whole seconds, rounded UP — never a wait shorter than the real one)
    /// and a ProblemDetails both clients show as written. The policy's own sentence, so a
    /// reporter is not told about sign-in attempts.
    /// </summary>
    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        int? retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : null;

        if (retryAfterSeconds is not null)
        {
            http.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }

        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var wait = retryAfterSeconds is null ? "a little later" : $"in {Describe(retryAfterSeconds.Value)}";

        var detail = policy == RateLimitSettings.ReportsPolicy
            ? $"You have filed the most reports allowed in an hour. Try again {wait}."
            : $"Too many sign-in attempts from this address. Try again {wait}.";

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await http.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = detail
        }, cancellationToken);
    }

    /// <summary>"45 seconds", "1 minute", "12 minutes" — whole minutes above a minute, rounded up.</summary>
    internal static string Describe(int seconds)
    {
        if (seconds < 60)
        {
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        var minutes = (seconds + 59) / 60;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
