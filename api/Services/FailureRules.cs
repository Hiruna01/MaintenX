namespace CampusFacilities.Api.Services;

/// <summary>
/// The repeat-failure and warranty rules, written once.
///
/// Two readers need them: the asset's own failure summary (AssetService) and the estate-wide
/// repeat-failure list (AnalyticsService). A second copy of either rule would be a second
/// answer waiting to disagree — an asset flagged on the analytics page and not on its own
/// page, or the other way round — so both call these.
///
/// Pure functions over dates and counts. "Today" is the caller's, read from TimeProvider.
/// </summary>
public static class FailureRules
{
    /// <summary>
    /// "Three months" means exactly 90 days. AddMonths(-3) would be a window of 89, 90, 91
    /// or 92 days depending on the month.
    /// </summary>
    public const int RepeatFailureWindowDays = 90;

    /// <summary>Visits inside the window that make an asset a repeat failure.</summary>
    public const int RepeatFailureVisits = 3;

    /// <summary>The first day inside the window — a visit ON this day counts.</summary>
    public static DateOnly RepeatFailureWindowStart(DateOnly today) =>
        today.AddDays(-RepeatFailureWindowDays);

    public static bool IsRepeatFailure(int visitsInWindow) => visitsInWindow >= RepeatFailureVisits;

    /// <summary>
    /// A null expiry is "none recorded", which is not "expired" but is not cover either.
    /// Expiring today still counts as covered — a warranty runs to the end of the day it
    /// expires on, which is exactly why the column is a DateOnly.
    /// </summary>
    public static bool IsUnderWarranty(DateOnly? warrantyExpiresOn, DateOnly today) =>
        warrantyExpiresOn is not null && warrantyExpiresOn >= today;
}
