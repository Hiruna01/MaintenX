using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — GET /api/analytics/metrics. Three answers a facilities manager needs, in
/// one read: do repairs hold, is the clarifier asking good questions, and which machines
/// keep failing.
///
/// EVERY FIGURE HERE IS A COUNT, A DATE COMPARISON OR ARITHMETIC ON THEM, done by
/// AnalyticsService in SQL and C#. No agent is called and no model output is read as a
/// number — the clarifier's steps are only looked at to learn THAT it ran.
///
/// Every rate is a PERCENTAGE, 0 to 100, rounded to two decimals, and is 0 — never NaN —
/// when there is nothing to divide by. Each one travels with the counts it was divided from,
/// so a client can tell "0% of nothing" from a real 0%.
/// </summary>
public record MetricsDto(
    // The range asked for, echoed back. Both null means all time. UTC calendar days, both
    // ends inclusive, the same arithmetic as the report and verification lists.
    DateOnly? FromDate,
    DateOnly? ToDate,

    ReopenRateMetricsDto ReopenRate,

    ClarificationMetricsDto Clarification,

    // The day the 90-day repeat-failure window ends on: ToDate when one was given, today
    // otherwise. With no range it is today, so this list agrees with every asset's own
    // failure summary.
    DateOnly RepeatFailuresAsOf,

    // Ranked by TotalCost, highest first — the list a manager acts on.
    IReadOnlyList<RepeatFailureAssetDto> RepeatFailures);

/// <summary>
/// Reopened checks as a share of ANSWERED checks — Confirmed + Reopened — overall, per asset
/// category and per month.
///
/// Answered, not every check: Pending, AwaitingReporterResponse, Expired and Escalated are
/// all checks nobody gave a verdict on, and folding silence into either column would report
/// a result that was never given. The same denominator as VerificationMetricsDto.ReopenRate,
/// through the same MetricRules.Percent.
///
/// A check falls in the range by its DueAt — the date every check has, and the one the
/// verification list filters on.
/// </summary>
public record ReopenRateMetricsDto(
    int Answered,
    int Reopened,
    decimal ReopenRate,

    // Categories with at least one answered check in the range, worst rate first. A
    // category with nothing answered is left out rather than shown at 0%, which would read
    // as "every repair held".
    IReadOnlyList<CategoryReopenRateDto> ByCategory,

    // Always six entries, oldest first: the month of ToDate (or of today) and the five
    // before it, the last one stopping at ToDate. FromDate does not trim it — a trend is a
    // fixed shape, and a month with nothing answered is an entry with Answered 0 rather
    // than a gap.
    IReadOnlyList<MonthlyReopenRateDto> MonthlyTrend);

public record CategoryReopenRateDto(
    int AssetCategoryId,
    string CategoryName,
    int Answered,
    int Reopened,
    decimal ReopenRate);

public record MonthlyReopenRateDto(
    int Year,
    int Month,
    int Answered,
    int Reopened,
    decimal ReopenRate);

/// <summary>
/// How well the clarifier is doing its one job — asking only what changes the outcome, and
/// asking it in a way reporters answer. Over reports FILED in the range (Report.CreatedAt).
/// </summary>
public record ClarificationMetricsDto(
    // Reports the clarifier actually ran on: a successful clarifier AGENT RUN, or questions
    // on the report. A failed run is not a report that "needed no questions", and neither
    // is one never clarified at all — so neither is in this count.
    int ReportsClarified,

    // Of those, how many it asked nothing about — the report was already clear.
    int ReportsWithNoQuestions,
    decimal NoQuestionRate,

    // Questions per report that needed any, i.e. QuestionsAsked / ReportsWithQuestions.
    int ReportsWithQuestions,
    int QuestionsAsked,
    decimal AverageQuestionsPerReport,

    int QuestionsAnswered,
    decimal AnswerRate,

    // Median hours from a question being asked to its answer, over answered questions.
    // NULL when nothing has been answered. Null is not zero: a median of 0 hours would say
    // reporters answer instantly, which is a claim, not an absence of data.
    double? MedianHoursToAnswer);

/// <summary>
/// One asset with FailureRules.RepeatFailureVisits (3) or more service visits in the 90 days
/// ending on MetricsDto.RepeatFailuresAsOf — the same rule, from the same class, as the
/// asset's own failure summary.
/// </summary>
public record RepeatFailureAssetDto(
    int AssetId,
    string AssetTag,
    string Name,
    int AssetCategoryId,
    string CategoryName,
    AssetStatus Status,

    // Visits inside the window.
    int FailureCount,
    DateOnly LastServicedOn,
    int DaysSinceLastService,

    // The ActualCost of the work orders behind those visits. A ServiceRecord carries no cost
    // of its own, and seeded or imported history was never produced by a work order — so a
    // visit with no order, or an order with no ActualCost, adds nothing here and is counted
    // in VisitsWithoutCost instead. TotalCost is what is KNOWN to have been spent.
    decimal TotalCost,
    int VisitsWithoutCost,

    // As of RepeatFailuresAsOf, by FailureRules.IsUnderWarranty — expiring that day still
    // counts, and no recorded warranty is false.
    bool IsUnderWarranty,
    DateOnly? WarrantyExpiresOn);
