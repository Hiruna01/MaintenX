namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for POST /api/timetable/sync — what one sync did, and how fresh the
/// ClassScheduleSlot cache is now.
///
/// ALWAYS A 200, including when Google could not be read. A failed sync is not a failed
/// request: the cache is still there, the slot finder still reads it, and what the caller
/// needs to know is how far to trust it. <see cref="Degraded"/> and
/// <see cref="FailureReason"/> say Google was not reached; <see cref="CacheAgeMinutes"/>
/// and <see cref="StalenessWarning"/> say what that costs.
/// </summary>
/// <param name="Degraded">True when Google was not read and the existing rows were left as they were.</param>
/// <param name="FailureReason">Why, when degraded; null otherwise. Sent by NAME.</param>
/// <param name="SyncedCount">Events written to the cache — inserted, updated or confirmed unchanged.</param>
/// <param name="RemovedCount">Rows removed because Google reported the class cancelled or moved out of every known room.</param>
/// <param name="SkippedCount">Events that could not be placed in a room — no times, or a location matching no room code (or more than one).</param>
/// <param name="LastSyncedAt">
/// The newest SyncedAt in the cache, UTC. NULL IS NOT ZERO: null means nothing has ever
/// been synced, and every room looks free to the slot finder.
/// </param>
/// <param name="CacheAgeMinutes">Whole minutes since <paramref name="LastSyncedAt"/>; null when it is null.</param>
/// <param name="IsStale">True when the cache is older than 24 hours, or empty.</param>
/// <param name="StalenessWarning">A sentence saying what staleness means for scheduling, when stale; null otherwise.</param>
public record TimetableSyncResultDto(
    bool Degraded,
    TimetableSyncFailure? FailureReason,
    int SyncedCount,
    int RemovedCount,
    int SkippedCount,
    DateTime? LastSyncedAt,
    int? CacheAgeMinutes,
    bool IsStale,
    string? StalenessWarning);

/// <summary>
/// Why a sync did not read Google. Sent as its NAME, like every enum in this API.
/// </summary>
public enum TimetableSyncFailure
{
    /// <summary>Google:CalendarId or Google:ServiceAccountJsonBase64 is not set.</summary>
    NotConfigured,

    /// <summary>Google did not answer inside Google:TimeoutSeconds.</summary>
    Timeout,

    /// <summary>Google answered 5xx — an outage on their side.</summary>
    GoogleServerError,

    /// <summary>
    /// The service account's token was refused, or Google answered 401/403 — a deleted or
    /// revoked key, or the Calendar API not enabled on the Google Cloud project.
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// Any other 4xx. Most often a 404: Google answers the same way for a CalendarId that
    /// does not exist and for one that has not been shared with the service account.
    /// </summary>
    Rejected,

    /// <summary>No HTTP answer at all — DNS, a refused connection, no network.</summary>
    Unreachable
}
