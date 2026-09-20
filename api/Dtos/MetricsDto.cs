namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — how well repairs are actually holding, counted across every verification
/// check.
///
/// This is the number the whole component exists to produce. Before it, "we completed 40
/// work orders" was the only measure available, and it counts a repair that failed a week
/// later exactly the same as one that worked.
///
/// EVERY FIGURE HERE IS COMPUTED IN C# FROM COUNTS, never estimated by a model. A rate a
/// model produced would be unauditable and occasionally wrong, and this one goes in front
/// of whoever decides what to replace.
/// </summary>
public record MetricsDto(
    int Total,

    // The open states, split because they mean different things operationally: Pending is
    // the system waiting out the delay, AwaitingReporterResponse is a person not replying.
    int Pending,
    int AwaitingReporterResponse,

    // The closed states.
    int Confirmed,
    int Reopened,
    int Escalated,
    int Expired,

    // A PERCENTAGE, 0 to 100, rounded to two decimals — not a 0-to-1 fraction.
    //
    // Confirmed as a share of checks that actually got an answer (Confirmed + Reopened),
    // NOT of Total. Expired checks are excluded from the denominator on purpose: nobody
    // answered them, and folding silence into either column would report a result that was
    // never given. decimal rather than double so the displayed figure is exact, and 0 when
    // nothing has been answered yet rather than a division by zero.
    decimal ConfirmationRate,

    // The same denominator, the other way up. Carried rather than left to the client to
    // work out, because "how often do repairs fail" is the figure this component exists
    // to surface and it should not depend on a subtraction someone might get wrong.
    decimal ReopenRate,

    // How long reporters take to answer once asked, in days. Null until at least one has —
    // an average of nothing is not zero.
    double? AverageDaysToRespond,

    // Checks whose DueAt has passed and which the sweep has not yet moved on. Steady growth
    // here means the sweep is not running, which is invisible in the counts above.
    int OverdueUnprocessed);
