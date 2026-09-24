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
}
