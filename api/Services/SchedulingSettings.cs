namespace CampusFacilities.Api.Services;

/// <summary>
/// When maintenance may be booked: the campus working day, the campus time zone that day is
/// measured in, and how much clear time a class needs either side of it.
///
/// FROM CONFIGURATION, never literals in a service — the same reasoning as ApprovalSettings
/// and VerificationSettings. Working hours change with a term, a faculty or a union
/// agreement, and a demo database may want them different from a real one.
///
/// THE TIME ZONE IS THE ONE THAT MATTERS MOST. Every booking and every class is stored in
/// UTC, but "08:00 to 17:00" is a statement about the clock on the lecture hall wall.
/// Measured in UTC, the working day in Colombo would run from 13:30 to 22:30. The zone is
/// resolved at startup in Program.cs, so a misspelt one stops the API booting rather than
/// failing the first time someone asks for a slot.
///
/// Built once in Program.cs and registered as a singleton: it holds no DbContext and never
/// changes after startup.
/// </summary>
public class SchedulingSettings
{
    /// <summary>
    /// IANA id — Sri Lanka, which has no daylight saving, so no local time is ever skipped
    /// or repeated. .NET 8 resolves IANA ids on every platform the team uses.
    /// </summary>
    public const string DefaultTimeZoneId = "Asia/Colombo";

    public static readonly TimeOnly DefaultWorkdayStart = new(8, 0);

    public static readonly TimeOnly DefaultWorkdayEnd = new(17, 0);

    public const int DefaultClassBufferMinutes = 15;

    public string TimeZoneId { get; init; } = DefaultTimeZoneId;

    /// <summary>Earliest local time a visit may start, Monday to Friday.</summary>
    public TimeOnly WorkdayStart { get; init; } = DefaultWorkdayStart;

    /// <summary>
    /// Latest local time a visit may END. Inclusive: a visit ending exactly at 17:00 is
    /// inside the working day, the same way a slot ending as the next begins does not
    /// overlap it.
    /// </summary>
    public TimeOnly WorkdayEnd { get; init; } = DefaultWorkdayEnd;

    /// <summary>
    /// Clear minutes required before a class starts and after it ends. A technician still
    /// packing up a ladder when the lecturer walks in is a conflict, even if the booked
    /// times technically do not overlap.
    /// </summary>
    public int ClassBufferMinutes { get; init; } = DefaultClassBufferMinutes;

    private TimeZoneInfo? _timeZone;

    public TimeZoneInfo TimeZone => _timeZone ??= TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
