namespace CampusFacilities.Api.Dtos;

/// <summary>
/// How GET /api/verifications orders its results. A query contract, not a persisted value,
/// so it lives here beside the DTOs rather than in Models — same as <see cref="ReportSort"/>.
/// Bound by NAME, so an unknown sort is a 400 from model binding.
/// </summary>
public enum VerificationSort
{
    /// <summary>
    /// Latest due date first — the default. A reporter's list reads from the question they
    /// were asked most recently, and a manager's from the checks falling due now rather
    /// than the ones settled months ago.
    /// </summary>
    DueAt,

    /// <summary>
    /// Grouped by the stored status NAME, so alphabetically (AwaitingReporterResponse,
    /// Confirmed, Escalated, ...), not by lifecycle position — the same consequence of
    /// storing enums as strings as ReportSort.Status, and for the same reason accepted.
    /// </summary>
    Status
}
