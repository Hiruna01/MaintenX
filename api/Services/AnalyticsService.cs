using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The estate-wide metrics behind GET /api/analytics/metrics. See MetricsDto.
///
/// NO AGENT ANYWHERE IN HERE. Every figure is a count, a date comparison or arithmetic on
/// them — deterministic business rules, which live in C#. The clarifier's steps are read
/// only to learn that a run happened, never for anything the model said.
///
/// Where the work happens is chosen per figure, and the split is always the same one:
///
///   * COUNTING AND GROUPING is SQL — GroupBy/Count through EF Core, one aggregated row per
///     group, so a table of thousands of checks comes back as a dozen rows.
///   * RATES, THE MEDIAN, MONEY AND DateOnly ARITHMETIC are C#, over those rows. PostgreSQL
///     has a median and SQLite (the default test database) does not; SQLite stores decimal
///     as TEXT, so a SUM of costs there would add strings; and DateOnly arithmetic does not
///     translate to SQLite at all. Doing these in memory is what makes both test databases
///     give the same answer — the same reasoning as the approval threshold and the failure
///     summary.
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    /// <summary>Months in the reopen-rate trend, the current one included.</summary>
    private const int TrendMonths = 6;

    /// <summary>
    /// The ValidationResult a successful agent run is recorded with (WorkflowRunner). A
    /// SafeFailure or CallFailed clarifier asked nothing because it could not, which is not
    /// the same as a report that needed nothing asked.
    /// </summary>
    private const string OkResult = "Ok";

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    /// <param name="time">
    /// Where "today" comes from — the end of the trend and of the repeat-failure window when
    /// no toDate is given. TimeProvider.System in the app; a fixed date in the tests.
    /// </param>
    public AnalyticsService(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<MetricsDto> GetMetricsAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        // The day both the trend and the repeat-failure window end on. With no range it is
        // today, so the repeat-failure list agrees with each asset's own failure summary.
        var asOf = toDate ?? today;

        // UTC calendar days, both ends inclusive: the bound is the day AFTER toDate,
        // exclusive, so nothing stamped during the last day of the range is dropped. The
        // same arithmetic as ReportService and VerificationService.
        var fromUtc = fromDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = toDate?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var reopenRate = await GetReopenRateAsync(fromUtc, toExclusiveUtc, asOf, cancellationToken);
        var clarification = await GetClarificationAsync(fromUtc, toExclusiveUtc, cancellationToken);
        var repeatFailures = await GetRepeatFailuresAsync(asOf, cancellationToken);

        return new MetricsDto(fromDate, toDate, reopenRate, clarification, asOf, repeatFailures);
    }

    // -----------------------------------------------------------------------
    // 1. Reopen rate
    // -----------------------------------------------------------------------

    private async Task<ReopenRateMetricsDto> GetReopenRateAsync(
        DateTime? fromUtc,
        DateTime? toExclusiveUtc,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        // ANSWERED checks only — the denominator is Confirmed + Reopened, never every check.
        // See ReopenRateMetricsDto.
        var answered = _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.Status == VerificationStatus.Confirmed || v.Status == VerificationStatus.Reopened);

        var inRange = answered;

        if (fromUtc is not null)
        {
            inRange = inRange.Where(v => v.DueAt >= fromUtc);
        }

        if (toExclusiveUtc is not null)
        {
            inRange = inRange.Where(v => v.DueAt < toExclusiveUtc);
        }

        // One grouped query gives both the per-category split and, summed, the overall
        // figure — so the overall rate is by construction the total of the categories and
        // the two cannot be assembled from rows that changed in between. Every check has
        // an asset and every asset a category, so nothing falls outside a group.
        var byCategoryRows = await inRange
            .GroupBy(v => new { v.Asset!.AssetCategoryId, v.Asset.Category!.Name, v.Status })
            .Select(g => new { g.Key.AssetCategoryId, g.Key.Name, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var byCategory = byCategoryRows
            .GroupBy(r => new { r.AssetCategoryId, r.Name })
            .Select(g =>
            {
                var total = g.Sum(r => r.Count);
                var reopened = g.Where(r => r.Status == VerificationStatus.Reopened).Sum(r => r.Count);
                return new CategoryReopenRateDto(
                    g.Key.AssetCategoryId, g.Key.Name, total, reopened, MetricRules.Percent(reopened, total));
            })
            // Worst first: the category whose repairs fail most is the one to look at.
            .OrderByDescending(c => c.ReopenRate)
            .ThenByDescending(c => c.Answered)
            .ThenBy(c => c.CategoryName)
            .ToList();

        var answeredCount = byCategory.Sum(c => c.Answered);
        var reopenedCount = byCategory.Sum(c => c.Reopened);

        // The trend: six calendar months ending with asOf's. fromDate does not trim it —
        // a trend is a fixed shape — but it does end where the range ends: with a toDate,
        // the last month stops at the end of that day rather than the end of the month.
        var lastMonth = new DateOnly(asOf.Year, asOf.Month, 1);
        var firstMonth = lastMonth.AddMonths(-(TrendMonths - 1));
        var trendFromUtc = firstMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var trendToExclusiveUtc = toExclusiveUtc
            ?? lastMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Grouped by the UTC year and month of DueAt, in SQL — date_part on PostgreSQL,
        // strftime on SQLite. Both are exact on a UTC timestamp.
        var trendRows = await answered
            .Where(v => v.DueAt >= trendFromUtc && v.DueAt < trendToExclusiveUtc)
            .GroupBy(v => new { v.DueAt.Year, v.DueAt.Month, v.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Every month present, empty ones included, so the list is always six long and a
        // chart of it never silently skips a month.
        var trend = Enumerable.Range(0, TrendMonths)
            .Select(i => firstMonth.AddMonths(i))
            .Select(month =>
            {
                var rows = trendRows.Where(r => r.Year == month.Year && r.Month == month.Month).ToList();
                var total = rows.Sum(r => r.Count);
                var reopened = rows.Where(r => r.Status == VerificationStatus.Reopened).Sum(r => r.Count);
                return new MonthlyReopenRateDto(
                    month.Year, month.Month, total, reopened, MetricRules.Percent(reopened, total));
            })
            .ToList();

        return new ReopenRateMetricsDto(
            answeredCount,
            reopenedCount,
            MetricRules.Percent(reopenedCount, answeredCount),
            byCategory,
            trend);
    }

    // -----------------------------------------------------------------------
    // 2. Clarification efficiency
    // -----------------------------------------------------------------------

    private async Task<ClarificationMetricsDto> GetClarificationAsync(
        DateTime? fromUtc,
        DateTime? toExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var reports = _db.Reports.AsNoTracking();

        if (fromUtc is not null)
        {
            reports = reports.Where(r => r.CreatedAt >= fromUtc);
        }

        if (toExclusiveUtc is not null)
        {
            reports = reports.Where(r => r.CreatedAt < toExclusiveUtc);
        }

        // Not executed on its own — a subquery inside each query below.
        var reportIds = reports.Select(r => r.Id);

        // Which reports the clarifier successfully ran on. Zero questions leaves no
        // ClarificationQuestion row behind, so "asked nothing" can only be told from "never
        // ran" or "ran and failed" by the step itself.
        //
        // The clarifier's TOOL CALLS are recorded under the same AgentName with the same
        // "Ok", so the agent run is picked out by its empty ToolCallsJson — AgentAnalysis's
        // test, applied in memory because the difference is inside a jsonb column.
        var clarifierSteps = await _db.AgentSteps
            .AsNoTracking()
            .Where(s => s.AgentName == AgentRunResponse.ClarifierAgentName
                        && s.ValidationResult == OkResult
                        && s.Workflow!.ReportId != null
                        && reportIds.Contains(s.Workflow.ReportId.Value))
            .Select(s => new { ReportId = s.Workflow!.ReportId!.Value, s.ToolCallsJson })
            .ToListAsync(cancellationToken);

        var askedPerReport = await _db.ClarificationQuestions
            .AsNoTracking()
            .Where(q => reportIds.Contains(q.ReportId))
            .GroupBy(q => q.ReportId)
            .Select(g => new { ReportId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ReportId, x => x.Count, cancellationToken);

        // Only the two timestamps the median needs, one row per ANSWERED question. The
        // subtraction is C#: DateTime arithmetic in SQL differs between the two providers.
        var answerTimes = await _db.ClarificationAnswers
            .AsNoTracking()
            .Where(a => reportIds.Contains(a.Question!.ReportId))
            .Select(a => new { AskedAt = a.Question!.CreatedAt, a.AnsweredAt })
            .ToListAsync(cancellationToken);

        // A report with questions was clarified whatever its step says; the union keeps a
        // report from being counted as asked-about but not clarified.
        var clarified = clarifierSteps
            .Where(s => AgentAnalysis.IsAgentRunStep(s.ToolCallsJson))
            .Select(s => s.ReportId)
            .Concat(askedPerReport.Keys)
            .ToHashSet();

        var withNoQuestions = clarified.Count(id => !askedPerReport.ContainsKey(id));
        var withQuestions = askedPerReport.Count;
        var questionsAsked = askedPerReport.Values.Sum();
        var questionsAnswered = answerTimes.Count;

        return new ClarificationMetricsDto(
            ReportsClarified: clarified.Count,
            ReportsWithNoQuestions: withNoQuestions,
            NoQuestionRate: MetricRules.Percent(withNoQuestions, clarified.Count),
            ReportsWithQuestions: withQuestions,
            QuestionsAsked: questionsAsked,
            AverageQuestionsPerReport: MetricRules.Ratio(questionsAsked, withQuestions),
            QuestionsAnswered: questionsAnswered,
            AnswerRate: MetricRules.Percent(questionsAnswered, questionsAsked),
            MedianHoursToAnswer: Median(answerTimes.Select(t => (t.AnsweredAt - t.AskedAt).TotalHours)));
    }

    /// <summary>
    /// The median, rounded to two decimals, or null for no values — a median of nothing is
    /// not zero. The middle two are averaged when the count is even.
    /// </summary>
    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();

        if (sorted.Count == 0)
        {
            return null;
        }

        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;

        return Math.Round(median, 2);
    }

    // -----------------------------------------------------------------------
    // 3. Repeat-failure detection
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<RepeatFailureAssetDto>> GetRepeatFailuresAsync(
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        // The window and the threshold are FailureRules' — the same ones the asset's own
        // failure summary reads — so an asset on this list always says IsRepeatFailure on
        // its own page, and the other way round. A DateOnly compared with a parameter
        // translates on both providers; it is ARITHMETIC on one that does not.
        var windowStart = FailureRules.RepeatFailureWindowStart(asOf);

        var inWindow = _db.ServiceRecords
            .AsNoTracking()
            .Where(s => s.ServicedOn >= windowStart && s.ServicedOn <= asOf);

        // The detection itself is one SQL aggregate: GROUP BY asset HAVING COUNT >= 3.
        var repeatAssetIds = await inWindow
            .GroupBy(s => s.AssetId)
            .Where(g => g.Count() >= FailureRules.RepeatFailureVisits)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        if (repeatAssetIds.Count == 0)
        {
            return [];
        }

        // The flagged assets' visits, to date and cost them in C#.
        var visits = await inWindow
            .Where(s => repeatAssetIds.Contains(s.AssetId))
            .Select(s => new { s.AssetId, s.ServicedOn, s.WorkOrderId })
            .ToListAsync(cancellationToken);

        // A ServiceRecord has no cost; the work order that produced it does. Summed in C#,
        // never in SQL, because SQLite stores decimal as TEXT.
        var workOrderIds = visits
            .Where(v => v.WorkOrderId is not null)
            .Select(v => v.WorkOrderId!.Value)
            .Distinct()
            .ToList();

        var actualCosts = await _db.WorkOrders
            .AsNoTracking()
            .Where(w => workOrderIds.Contains(w.Id))
            .Select(w => new { w.Id, w.ActualCost })
            .ToDictionaryAsync(w => w.Id, w => w.ActualCost, cancellationToken);

        var assets = await _db.Assets
            .AsNoTracking()
            .Where(a => repeatAssetIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.AssetTag,
                a.Name,
                a.AssetCategoryId,
                CategoryName = a.Category!.Name,
                a.Status,
                a.WarrantyExpiresOn
            })
            .ToListAsync(cancellationToken);

        var visitsByAsset = visits.ToLookup(v => v.AssetId);

        return assets
            .Select(a =>
            {
                var assetVisits = visitsByAsset[a.Id].ToList();
                var lastServicedOn = assetVisits.Max(v => v.ServicedOn);

                decimal? CostOf(int? workOrderId) =>
                    workOrderId is not null && actualCosts.TryGetValue(workOrderId.Value, out var cost)
                        ? cost
                        : null;

                // Each work order's cost once, even if two visits were ever filed against
                // the same order.
                var totalCost = assetVisits
                    .Where(v => v.WorkOrderId is not null)
                    .Select(v => v.WorkOrderId)
                    .Distinct()
                    .Sum(id => CostOf(id) ?? 0m);

                return new RepeatFailureAssetDto(
                    a.Id,
                    a.AssetTag,
                    a.Name,
                    a.AssetCategoryId,
                    a.CategoryName,
                    a.Status,
                    FailureCount: assetVisits.Count,
                    LastServicedOn: lastServicedOn,
                    DaysSinceLastService: asOf.DayNumber - lastServicedOn.DayNumber,
                    TotalCost: totalCost,
                    VisitsWithoutCost: assetVisits.Count(v => CostOf(v.WorkOrderId) is null),
                    IsUnderWarranty: FailureRules.IsUnderWarranty(a.WarrantyExpiresOn, asOf),
                    WarrantyExpiresOn: a.WarrantyExpiresOn);
            })
            // Ranked by money, then by how often it failed, then Id so the order is total.
            .OrderByDescending(r => r.TotalCost)
            .ThenByDescending(r => r.FailureCount)
            .ThenBy(r => r.AssetId)
            .ToList();
    }
}
