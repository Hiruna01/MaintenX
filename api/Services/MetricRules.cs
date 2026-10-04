namespace CampusFacilities.Api.Services;

/// <summary>
/// Arithmetic shared by every metrics read, so a rate means the same thing wherever it
/// appears.
/// </summary>
public static class MetricRules
{
    /// <summary>
    /// <paramref name="part"/> as a PERCENTAGE of <paramref name="whole"/>, 0 to 100, rounded
    /// to two decimals. decimal rather than double so the figure shown is the figure computed.
    ///
    /// Zero when there is nothing to divide by — never NaN, never a DivideByZeroException.
    /// A client tells "0% of nothing" from a real 0% by the denominator carried beside every
    /// rate, which is why each DTO carries its counts as well as its rate.
    /// </summary>
    public static decimal Percent(int part, int whole) =>
        whole == 0 ? 0m : Math.Round(part * 100m / whole, 2);

    /// <summary>The same guard for a plain ratio (an average), rounded to two decimals.</summary>
    public static decimal Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0m : Math.Round((decimal)numerator / denominator, 2);

    /// <summary>
    /// The median of whole milliseconds, or null for no values — a median of nothing is not
    /// zero. The middle two are averaged when the count is even, rounded half away from zero
    /// to a whole millisecond. The same definition as AnalyticsService's median.
    /// </summary>
    public static int? MedianMs(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (int)Math.Round((sorted[middle - 1] + (long)sorted[middle]) / 2m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The nearest-rank percentile: the smallest value with at least
    /// <paramref name="percent"/>% of the values at or below it — always a value that was
    /// actually observed, never an interpolation. Null for no values.
    /// </summary>
    public static int? PercentileMs(IReadOnlyList<int> values, int percent)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToList();

        // ceil(percent / 100 × n), in integers so no double is involved; a rank of at least 1.
        var rank = Math.Max(1, (percent * sorted.Count + 99) / 100);
        return sorted[rank - 1];
    }
}
