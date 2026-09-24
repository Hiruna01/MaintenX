namespace CampusFacilities.Api.Services;

/// <summary>
/// Configuration for the campus timetable sync from Google Calendar.
///
/// A SERVICE ACCOUNT, NOT OAUTH. There is no consent screen, no user login and no per-user
/// token: the timetable calendar is shared (read-only) with the service account's email
/// address, and the API reads it as that account. One credential, owned by the server,
/// never seen by a client.
///
/// The key file is supplied BASE64-ENCODED (Google:ServiceAccountJsonBase64, or
/// GOOGLE_SERVICE_ACCOUNT_JSON_BASE64) because a multi-line JSON document with a PEM private
/// key inside it does not survive being pasted into an environment variable on a hosting
/// dashboard. Program.cs decodes it once at startup; this class holds the decoded JSON. The
/// key itself is never committed — user-secrets locally, an environment variable on Render.
///
/// Like AgentSettings and StorageSettings, an empty value does not stop the API booting: a
/// team member not working on scheduling should not need a Google Cloud project to run it.
/// The sync then reports itself degraded, and the slot finder keeps reading whatever
/// ClassScheduleSlot rows it already has.
///
/// Built once in Program.cs and registered as a singleton: it holds no DbContext and never
/// changes after startup.
/// </summary>
public class GoogleCalendarSettings
{
    /// <summary>Default seconds allowed for one whole sync's worth of Google calls.</summary>
    public const double DefaultTimeoutSeconds = 10;

    /// <summary>Default minutes between scheduled syncs.</summary>
    public const int DefaultSyncIntervalMinutes = 60;

    /// <summary>
    /// How old the mirror may get before a sync response carries a staleness warning.
    /// A timetable changes by the week, not by the minute, so a day-old copy is still
    /// probably right — but "probably" is worth saying out loud once it is more than a day.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    /// <summary>
    /// How far ahead each sync mirrors: about a semester. A slot search further out than
    /// this sees no classes at all, so the window is deliberately longer than anyone plans
    /// maintenance ahead.
    /// </summary>
    public const int SyncDaysAhead = 180;

    /// <summary>
    /// The calendar to read, as Google shows it under Settings → Integrate calendar →
    /// Calendar ID (something like abc123@group.calendar.google.com).
    /// </summary>
    public string CalendarId { get; init; } = string.Empty;

    /// <summary>The service account key file's JSON, already decoded from base64.</summary>
    public string ServiceAccountJson { get; init; } = string.Empty;

    /// <summary>
    /// How long one sync may wait on Google before giving up and serving the cache.
    ///
    /// Short on purpose, unlike the agent's 60 seconds. A manager pressing "sync" is
    /// waiting on this request, and the whole design is that a Google outage costs
    /// freshness, never availability — ten seconds of silence already says Google is not
    /// going to answer in a useful time.
    /// </summary>
    public double TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>Minutes between syncs run by <see cref="TimetableSyncWorker"/>.</summary>
    public int SyncIntervalMinutes { get; init; } = DefaultSyncIntervalMinutes;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CalendarId) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
