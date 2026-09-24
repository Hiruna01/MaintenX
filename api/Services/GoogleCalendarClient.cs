using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Http;
using Google.Apis.Services;

namespace CampusFacilities.Api.Services;

/// <summary>
/// <see cref="IGoogleCalendarClient"/> over Google.Apis.Calendar.v3, authenticated as a
/// service account. The only class in the API that constructs a Google client.
///
/// A singleton, like IWorkflowQueue: it holds no DbContext, and holding the CalendarService
/// for the life of the process means the service account's access token is fetched once an
/// hour and reused, rather than once per sync.
/// </summary>
public sealed class GoogleCalendarClient : IGoogleCalendarClient, IDisposable
{
    /// <summary>
    /// Read-only, and the narrowest scope the Calendar API offers. The calendar is only
    /// shared with the service account for reading anyway; asking for less than that is a
    /// second, independent guarantee that this system never writes to the timetable.
    /// </summary>
    private static readonly string[] Scopes = { CalendarService.Scope.CalendarReadonly };

    /// <summary>Google's maximum page size for events.list.</summary>
    private const int PageSize = 2500;

    /// <summary>
    /// A backstop against a calendar that pages forever. A semester of a whole campus's
    /// lectures is a few thousand events — one or two pages — so hitting this means
    /// something is wrong, and the timeout would stop it shortly after anyway.
    /// </summary>
    private const int MaxPages = 10;

    private readonly GoogleCalendarSettings _settings;
    private readonly CalendarService? _service;

    public GoogleCalendarClient(GoogleCalendarSettings settings)
    {
        _settings = settings;

        if (!settings.IsConfigured)
        {
            return;
        }

        _service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = CreateCredential(settings.ServiceAccountJson).ToGoogleCredential().CreateScoped(Scopes),
            ApplicationName = "MaintenX",
            // Google's own per-request timeout, in step with the sync's overall one.
            HttpClientTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            // By default the client retries a 503 with exponential back-off, which would
            // quietly spend the whole timeout waiting on a Google that is down. No retry
            // here: the next scheduled sync IS the retry, and meanwhile the cache serves.
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
        });
    }

    /// <summary>
    /// Parses a service account key. Called by Program.cs at startup as well as here, so a
    /// key that is not a service account key — an OAuth client secret downloaded by
    /// mistake, say — stops the API booting with a clear message instead of failing the
    /// first sync.
    ///
    /// CredentialFactory.FromJson&lt;ServiceAccountCredential&gt; rather than the older
    /// GoogleCredential.FromJson: it refuses every other credential type, including the
    /// "external account" configs that can make the API fetch arbitrary URLs.
    /// </summary>
    public static ServiceAccountCredential CreateCredential(string serviceAccountJson) =>
        CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson);

    public async Task<IReadOnlyList<Event>> ListEventsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        if (_service is null)
        {
            // GoogleCalendarSyncService checks IsConfigured before calling, so this is a
            // caller bug rather than a Google failure.
            throw new InvalidOperationException("Google Calendar is not configured.");
        }

        var events = new List<Event>();
        string? pageToken = null;
        var pages = 0;

        do
        {
            var request = _service.Events.List(_settings.CalendarId);
            request.TimeMinDateTimeOffset = new DateTimeOffset(from, TimeSpan.Zero);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(to, TimeSpan.Zero);
            // One row per occurrence: a weekly lecture comes back as each week's instance,
            // each with its own id, which is what ClassScheduleSlot.ExternalEventId holds.
            request.SingleEvents = true;
            request.ShowDeleted = true;
            request.MaxResults = PageSize;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(cancellationToken);

            if (page.Items is not null)
            {
                events.AddRange(page.Items);
            }

            pageToken = page.NextPageToken;
            pages++;
        }
        while (!string.IsNullOrEmpty(pageToken) && pages < MaxPages);

        return events;
    }

    public void Dispose() => _service?.Dispose();
}
