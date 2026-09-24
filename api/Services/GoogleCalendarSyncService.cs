using System.Net;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

/// <summary>
/// <see cref="ITimetableSyncService"/> from a Google Calendar ("Campus Timetable"), read as
/// a service account through <see cref="IGoogleCalendarClient"/>.
///
/// AN EVENT BECOMES A CLASS BY ITS LOCATION. Each event's location field holds a room CODE
/// ("MAB-101"), matched exactly apart from case and surrounding spaces. Anything else — a
/// location naming no room, or naming a code two rooms share — is skipped and logged,
/// never guessed at: a class placed in the wrong room makes that room look busy and the
/// real one look free, which is worse than not placing it at all.
/// </summary>
public class GoogleCalendarSyncService : ITimetableSyncService
{
    /// <summary>
    /// One sync at a time in this process. The hourly worker and a manager's button can
    /// otherwise fire together, both see the same new event as missing, and both insert it
    /// — the unique index on ExternalEventId would stop the second, but as a 500. Static
    /// because the service is scoped: every instance must share the one lock.
    /// </summary>
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    /// <summary>Column limits on ClassScheduleSlot, mirrored so a long title is cut rather than refused.</summary>
    private const int MaxTitleLength = 200;
    private const int MaxExternalIdLength = 200;

    private readonly AppDbContext _db;
    private readonly IGoogleCalendarClient _calendar;
    private readonly GoogleCalendarSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<GoogleCalendarSyncService> _logger;

    public GoogleCalendarSyncService(
        AppDbContext db,
        IGoogleCalendarClient calendar,
        GoogleCalendarSettings settings,
        TimeProvider time,
        ILogger<GoogleCalendarSyncService> logger)
    {
        _db = db;
        _calendar = calendar;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<TimetableSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        await SyncLock.WaitAsync(cancellationToken);

        try
        {
            return await SyncLockedAsync(cancellationToken);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<TimetableSyncResultDto> SyncLockedAsync(CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning(
                "Timetable sync skipped: Google Calendar is not configured "
                + "(Google:CalendarId / Google:ServiceAccountJsonBase64). Serving the existing timetable.");
            return await DegradedAsync(TimetableSyncFailure.NotConfigured, cancellationToken);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var fetched = await FetchAsync(now, cancellationToken);

        if (fetched.Failure is { } failure)
        {
            return await DegradedAsync(failure, cancellationToken);
        }

        var (synced, removed, skipped) = await ApplyAsync(fetched.Events!, now, cancellationToken);

        _logger.LogInformation(
            "Timetable sync: {Synced} class(es) synced, {Removed} removed, {Skipped} skipped.",
            synced, removed, skipped);

        return await ResultAsync(degraded: false, failure: null, synced, removed, skipped, cancellationToken);
    }

    /// <summary>
    /// The one Google call, under THE EXPLICIT TIMEOUT, with every way it can fail turned
    /// into a <see cref="TimetableSyncFailure"/>. Nothing Google throws gets past here.
    ///
    /// The timeout covers the whole fetch — token exchange and every page — not each
    /// request separately, so "ten seconds" means ten seconds of the caller's time.
    /// </summary>
    private async Task<(IReadOnlyList<Event>? Events, TimetableSyncFailure? Failure)> FetchAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        try
        {
            var events = await _calendar.ListEventsAsync(
                now, now.AddDays(GoogleCalendarSettings.SyncDaysAhead), timeout.Token);
            return (events, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Either our CancelAfter or Google's own HttpClient timeout. The `when` keeps a
            // caller who genuinely hung up a cancellation rather than a Google failure.
            _logger.LogWarning(
                "Timetable sync: Google Calendar did not answer within {TimeoutSeconds}s. "
                + "Serving the existing timetable.", _settings.TimeoutSeconds);
            return (null, TimetableSyncFailure.Timeout);
        }
        catch (TokenResponseException ex)
        {
            // The token endpoint refused the service account: a deleted or revoked key.
            _logger.LogWarning(
                "Timetable sync: Google refused the service account credential ({Error}). "
                + "Serving the existing timetable.", ex.Error?.Error);
            return (null, TimetableSyncFailure.AuthenticationFailed);
        }
        catch (GoogleApiException ex)
        {
            var failure = Classify(ex.HttpStatusCode);

            _logger.LogWarning(
                "Timetable sync: Google Calendar answered HTTP {StatusCode} ({Failure}): {Message}. "
                + "Serving the existing timetable.", (int)ex.HttpStatusCode, failure, ex.Message);

            if (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Timetable sync: a 404 usually means calendar {CalendarId} has not been shared "
                    + "with the service account's email address.", _settings.CalendarId);
            }

            return (null, failure);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Timetable sync: could not reach Google Calendar. Serving the existing timetable.");
            return (null, TimetableSyncFailure.Unreachable);
        }
    }

    /// <summary>
    /// A status code Google answered with, as a failure. 401 and 403 are credentials or
    /// permissions; 5xx is Google's own outage; any other 4xx is a request Google will not
    /// serve, most often an unknown or unshared calendar.
    /// </summary>
    private static TimetableSyncFailure Classify(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => TimetableSyncFailure.AuthenticationFailed,
        >= 500 => TimetableSyncFailure.GoogleServerError,
        _ => TimetableSyncFailure.Rejected
    };

    /// <summary>
    /// Upserts every event that places a class in a room, and removes a row only on
    /// POSITIVE EVIDENCE — Google reporting that event cancelled, or moved somewhere that
    /// is not one of our rooms.
    ///
    /// NEVER ON ABSENCE. An event that simply is not in the reply keeps its row, because a
    /// missing class makes its room look free, and booking maintenance into an occupied
    /// lecture theatre is the mistake this table exists to prevent. An empty reply from a
    /// mistyped calendar id therefore cannot wipe the timetable; at worst a lecture that was
    /// deleted outright lingers, keeping a room looking busy that is actually free.
    /// </summary>
    private async Task<(int Synced, int Removed, int Skipped)> ApplyAsync(
        IReadOnlyList<Event> events,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var roomIdsByCode = await RoomIdsByCodeAsync(cancellationToken);

        // Last one wins if Google ever repeats an id across pages.
        var eventsById = new Dictionary<string, Event>();
        foreach (var e in events.Where(e => !string.IsNullOrEmpty(e.Id)))
        {
            eventsById[e.Id] = e;
        }

        var ids = eventsById.Keys.ToList();
        var existing = await _db.ClassScheduleSlots
            .Where(c => ids.Contains(c.ExternalEventId))
            .ToDictionaryAsync(c => c.ExternalEventId, cancellationToken);

        int synced = 0, removed = 0, skipped = 0;

        foreach (var (id, e) in eventsById)
        {
            var cancelled = string.Equals(e.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
            var mapped = cancelled ? null : Map(e, roomIdsByCode);

            if (mapped is not null)
            {
                if (existing.TryGetValue(id, out var row))
                {
                    // Assigned whether or not they changed: EF compares each value with what
                    // it loaded and marks only real changes. SyncedAt always changes, and
                    // AppDbContext leaves UpdatedAt alone when it is the only thing that did.
                    row.RoomId = mapped.RoomId;
                    row.StartsAt = mapped.StartsAt;
                    row.EndsAt = mapped.EndsAt;
                    row.Title = mapped.Title;
                    row.SyncedAt = now;
                }
                else
                {
                    mapped.ExternalEventId = id;
                    mapped.SyncedAt = now;
                    _db.ClassScheduleSlots.Add(mapped);
                }

                synced++;
                continue;
            }

            if (existing.TryGetValue(id, out var gone))
            {
                // Google has told us this class is not in any room we hold any more.
                _db.ClassScheduleSlots.Remove(gone);
                removed++;
            }

            if (!cancelled)
            {
                skipped++;
                _logger.LogWarning(
                    "Timetable sync skipped event {EventId} '{Title}': location '{Location}' "
                    + "does not match exactly one room code, or the event has no start and end time.",
                    id, e.Summary, e.Location);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return (synced, removed, skipped);
    }

    /// <summary>
    /// Room codes that identify exactly one room, case-insensitively. A code two rooms share
    /// is left out altogether, so an event naming it is skipped rather than placed in
    /// whichever room happened to come first.
    /// </summary>
    private async Task<Dictionary<string, int>> RoomIdsByCodeAsync(CancellationToken cancellationToken)
    {
        var rooms = await _db.Rooms
            .AsNoTracking()
            .Select(r => new { r.Id, r.Code })
            .ToListAsync(cancellationToken);

        return rooms
            .GroupBy(r => r.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An event as a class, or null when it cannot be one: an all-day event (a lecture has a
    /// start and an end time, a holiday banner does not), an end not after its start, an id
    /// too long for the column, or a location that is not exactly one room's code.
    /// </summary>
    private static ClassScheduleSlot? Map(Event e, IReadOnlyDictionary<string, int> roomIdsByCode)
    {
        var starts = e.Start?.DateTimeDateTimeOffset;
        var ends = e.End?.DateTimeDateTimeOffset;

        if (starts is null || ends is null || ends <= starts || e.Id.Length > MaxExternalIdLength)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(e.Location)
            || !roomIdsByCode.TryGetValue(e.Location.Trim(), out var roomId))
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(e.Summary) ? "Class" : e.Summary.Trim();

        return new ClassScheduleSlot
        {
            RoomId = roomId,
            StartsAt = starts.Value.UtcDateTime,
            EndsAt = ends.Value.UtcDateTime,
            Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title
        };
    }

    private Task<TimetableSyncResultDto> DegradedAsync(
        TimetableSyncFailure failure,
        CancellationToken cancellationToken) =>
        ResultAsync(degraded: true, failure, synced: 0, removed: 0, skipped: 0, cancellationToken);

    /// <summary>
    /// The sync's counts plus how fresh the cache is now, read from the rows themselves:
    /// the age of the cache is the age of its newest SyncedAt. That is the same figure after
    /// a success (a minute or so) and after a failure (however long Google has been out).
    /// </summary>
    private async Task<TimetableSyncResultDto> ResultAsync(
        bool degraded,
        TimetableSyncFailure? failure,
        int synced,
        int removed,
        int skipped,
        CancellationToken cancellationToken)
    {
        var lastSyncedAt = await _db.ClassScheduleSlots
            .MaxAsync(c => (DateTime?)c.SyncedAt, cancellationToken);

        if (lastSyncedAt is null)
        {
            // Null, not zero: an empty cache is not a fresh one.
            return new TimetableSyncResultDto(
                degraded, failure, synced, removed, skipped,
                LastSyncedAt: null,
                CacheAgeMinutes: null,
                IsStale: true,
                StalenessWarning: "No timetable has ever been synced, so every room looks free to "
                    + "the slot finder. Offered slots may clash with classes until a sync succeeds.");
        }

        // SQLite hands DateTimes back with no Kind; every one stored here is UTC.
        var last = DateTime.SpecifyKind(lastSyncedAt.Value, DateTimeKind.Utc);
        var age = _time.GetUtcNow().UtcDateTime - last;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var stale = age > GoogleCalendarSettings.StaleAfter;

        return new TimetableSyncResultDto(
            degraded, failure, synced, removed, skipped,
            LastSyncedAt: last,
            CacheAgeMinutes: (int)age.TotalMinutes,
            IsStale: stale,
            StalenessWarning: stale
                ? $"The timetable was last synced {(int)age.TotalHours} hours ago, more than "
                  + $"{(int)GoogleCalendarSettings.StaleAfter.TotalHours}. Offered slots may clash "
                  + "with classes added or moved since then."
                : null);
    }
}
