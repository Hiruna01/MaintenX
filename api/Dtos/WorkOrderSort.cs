namespace CampusFacilities.Api.Dtos;

/// <summary>
/// How GET /api/workorders orders its results. A query contract, not a persisted value, so
/// it lives here beside the DTOs rather than in Models — same as <see cref="ReportSort"/>.
/// Bound by NAME, so an unknown sort is a 400 from model binding.
/// </summary>
public enum WorkOrderSort
{
    /// <summary>Newest first — the default. A list of work orders is a worklist, read from the top.</summary>
    CreatedAt,

    /// <summary>
    /// Most expensive estimate first — what a manager reads to see where the money is going.
    ///
    /// SORTED IN C#, NOT IN SQL. SQLite (the default test mode) has no decimal type and
    /// refuses to ORDER BY one, and casting to a double to get round that would put money
    /// through a binary float — the one thing this project refuses to do with it. See
    /// WorkOrderService.GetAllAsync.
    /// </summary>
    Cost
}
