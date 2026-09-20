namespace CampusFacilities.Api.Services;

/// <summary>
/// How long the system waits before asking whether a repair held, and how often it looks
/// for checks that have come due.
///
/// BOTH FROM CONFIGURATION, never literals in a service. The delay is a policy decision
/// that will be argued about — five days is long enough for an intermittent fault to
/// resurface and short enough that the reporter still remembers the fault — and a service
/// that hardcoded it would need a code change, a review and a deploy to follow a decision
/// made in a meeting. It also could not be shortened for a demo, which is exactly when a
/// five-day wait is least convenient.
///
/// Built once in Program.cs and registered as a singleton: it holds no DbContext and never
/// changes after startup, exactly like JwtSettings, AgentSettings and ApprovalSettings.
/// </summary>
public class VerificationSettings
{
    /// <summary>
    /// Default days between a work order completing and its check falling due. One
    /// literal, in one place — Program.cs falls back to this constant rather than
    /// repeating the number, so the default cannot drift between the two.
    /// </summary>
    public const int DefaultDelayDays = 5;

    /// <summary>Default minutes between sweeps.</summary>
    public const int DefaultSweepIntervalMinutes = 60;

    /// <summary>
    /// Days after completion that a verification check falls due.
    ///
    /// The delay is the entire reason this component works. Asked the same afternoon,
    /// every reporter says yes, because an intermittent fault has not had the chance to
    /// come back yet — and a confirmation that means nothing is worse than no confirmation,
    /// because it goes into the metrics as a success.
    /// </summary>
    public int DelayDays { get; init; } = DefaultDelayDays;

    /// <summary>
    /// Minutes between sweeps for checks that have come due.
    ///
    /// Hourly is deliberately unhurried. Nothing here is time-critical — a check that has
    /// waited five days is not harmed by waiting another forty minutes — and a tight
    /// interval would mean a query against every pending row every few seconds for no
    /// benefit anyone could observe.
    /// </summary>
    public int SweepIntervalMinutes { get; init; } = DefaultSweepIntervalMinutes;
}
