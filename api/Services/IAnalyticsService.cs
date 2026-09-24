using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IAnalyticsService
{
    /// <summary>
    /// The estate-wide metrics: reopen rate (overall, per category, six-month trend),
    /// clarification efficiency, and the repeat-failure list. See MetricsDto for what each
    /// figure means and which date it is filtered on.
    ///
    /// <paramref name="fromDate"/> and <paramref name="toDate"/> are UTC calendar days, both
    /// ends inclusive, either may be omitted. The caller checks from &lt;= to.
    ///
    /// Counts and grouping run as SQL aggregates; percentages, the median, money and DateOnly
    /// arithmetic run in C# over the aggregated rows. No agent is involved, and every
    /// division is guarded — an empty database returns zeros, never NaN.
    /// </summary>
    Task<MetricsDto> GetMetricsAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default);
}
