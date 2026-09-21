namespace CampusFacilities.Api.Dtos;

/// <summary>
/// How GET /api/reports orders its results. A query contract, not a persisted value, so it
/// lives here beside the DTOs rather than in Models — same as <see cref="AssetSort"/>.
///
/// An enum rather than a free string because the value reaches an ORDER BY: model binding
/// already returns 400 for a member that does not exist, so an unknown sort is a refusal
/// rather than an ordering that silently falls back to something else. Bound by NAME, like
/// every other enum in this API.
/// </summary>
public enum ReportSort
{
    /// <summary>
    /// Newest first — the default, because a list of faults is a worklist and the one
    /// reported this morning matters more than the one from March. The opposite of
    /// AssetSort's default, and for the opposite reason: an asset list is scanned
    /// alphabetically, a report list is read from the top.
    /// </summary>
    CreatedAt,

    /// <summary>
    /// Grouped by where the fault has got to, in the enum's own declaration order —
    /// Submitted first, Closed last — which is the order the lifecycle runs in. Ordered on
    /// the stored STRING, so see the note in ReportService: the two are not the same.
    /// </summary>
    Status
}
