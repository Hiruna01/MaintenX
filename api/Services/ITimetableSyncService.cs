using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

/// <summary>
/// Mirrors the campus timetable into ClassScheduleSlot.
///
/// THIS IS THE ONLY READER OF THE TIMETABLE'S SOURCE. The slot finder in WorkOrderService
/// reads ClassScheduleSlot rows and never calls Google, so a Google outage costs the
/// scheduler freshness, never availability: it keeps offering slots from the last good
/// copy, and this service's response says how old that copy is.
/// </summary>
public interface ITimetableSyncService
{
    /// <summary>
    /// Pulls the next <see cref="GoogleCalendarSettings.SyncDaysAhead"/> days of classes
    /// and upserts them on ExternalEventId, so a re-sync updates rows rather than adding
    /// duplicates, and stamps SyncedAt on every row it confirms.
    ///
    /// NEVER THROWS FOR A GOOGLE FAILURE. A timeout, a 5xx, a refused credential, an
    /// unreachable host or missing configuration all come back as a degraded result with
    /// the existing rows untouched — the caller is either a manager's request or a
    /// background timer, and neither should be broken by somebody else's outage.
    /// </summary>
    Task<TimetableSyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
