namespace CampusFacilities.Api.Services;

/// <summary>
/// The repair SLA: how long an approved work order has to be completed.
///
/// FROM CONFIGURATION, never a literal in a service — the same reasoning as ApprovalSettings
/// and VerificationSettings. How fast a campus promises to fix things is a policy decision
/// that changes with staffing and budget, and a demo has to be able to shorten it.
///
/// Built once in Program.cs and registered as a singleton: it holds no DbContext and never
/// changes after startup.
/// </summary>
public class SlaSettings
{
    /// <summary>
    /// Default calendar days from approval to completion. One literal, in one place —
    /// Program.cs falls back to this constant rather than repeating the number.
    /// </summary>
    public const int DefaultResolutionDays = 7;

    /// <summary>
    /// Days an approved order has before it is overdue. Calendar days, not working days:
    /// public holidays are not modelled anywhere in this system (see SchedulingSettings), and
    /// a rule that skipped weekends but not holidays would be precise about the wrong thing.
    /// </summary>
    public int ResolutionDays { get; init; } = DefaultResolutionDays;
}
