using Google.Apis.Calendar.v3.Data;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The API's outbound call to Google Calendar — the same role IAgentClient plays for the
/// agent service. It fetches events and does nothing else: no room mapping, no database,
/// no decision about what a failure means.
///
/// UNLIKE IAgentClient, IT THROWS. A timeout, a Google 5xx and a refused credential are
/// three different answers the sync has to report differently, and the exception types are
/// what tells them apart. GoogleCalendarSyncService catches every one of them and turns it
/// into a degraded result, so nothing reaches a request or the background worker.
///
/// It exists as an interface so tests can stand in for Google — nothing in a test run can
/// reach a real calendar.
/// </summary>
public interface IGoogleCalendarClient
{
    /// <summary>
    /// Every event on the configured calendar that ends after <paramref name="from"/> and
    /// starts before <paramref name="to"/>, with recurring events expanded into their
    /// individual occurrences and CANCELLED ones included — a cancellation is the only
    /// positive evidence that a class is no longer on, so the sync needs to see it.
    /// </summary>
    Task<IReadOnlyList<Event>> ListEventsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
