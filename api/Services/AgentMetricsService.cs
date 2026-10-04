using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The agent monitoring figures behind GET /api/analytics/agents. See AgentMetricsDto.
///
/// Read-only over AgentStep, and NO AGENT IS CALLED. The runners already record, on every
/// agent-level step, what happened (ValidationResult), how long the agent took (DurationMs),
/// whether it retried (Attempts) and the tokens the provider reported (PromptTokens /
/// CompletionTokens). This service only counts and sums those columns.
///
/// The query selects the five agents' rows in the date range with only the columns it needs
/// — never PayloadJson — and everything after that is C#: telling an agent-run row from a
/// tool-call row means reading ToolCallsJson, a jsonb column (AgentAnalysis.IsAgentRunStep,
/// the same test the approval queue uses); percentiles do not exist in SQLite; and cost is
/// decimal money, which SQLite stores as TEXT. A campus produces thousands of steps, not
/// millions, so the rows fit in memory comfortably.
/// </summary>
public class AgentMetricsService : IAgentMetricsService
{
    /// <summary>
    /// The agent-level step names, in pipeline order — the order the page lists them in.
    /// The verification agent is last: it runs on its own path, weeks after the others.
    /// </summary>
    public static readonly IReadOnlyList<string> AgentNames =
    [
        PlanRules.PlannerAgentName,
        AgentRunResponse.ClarifierAgentName,
        AgentRunResponse.DiagnosticAgentName,
        AgentRunResponse.StrategistAgentName,
        AgentRunResponse.VerificationAgentName
    ];

    /// <summary>Days in the daily series, the last one included.</summary>
    public const int DailyDays = 30;

    /// <summary>How many runs the slowest and costliest lists carry.</summary>
    public const int RunListSize = 10;

    // The ValidationResult strings the runners write on an agent-level step.
    private const string Ok = "Ok";
    private const string SafeFailure = "SafeFailure";
    private const string CallFailed = "CallFailed";
    private const string Rejected = "Rejected";

    private readonly AppDbContext _db;
    private readonly LlmPricingSettings _pricing;
    private readonly TimeProvider _time;

    public AgentMetricsService(AppDbContext db, LlmPricingSettings pricing, TimeProvider time)
    {
        _db = db;
        _pricing = pricing;
        _time = time;
    }

    public async Task<AgentMetricsDto> GetAgentMetricsAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        // UTC calendar days, both ends inclusive: the bound is the day AFTER toDate,
        // exclusive — the same arithmetic as AnalyticsService.
        var fromUtc = fromDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = toDate?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var names = AgentNames.ToList();
        var query = _db.AgentSteps.AsNoTracking().Where(s => names.Contains(s.AgentName));

        if (fromUtc is not null)
        {
            query = query.Where(s => s.CreatedAt >= fromUtc);
        }

        if (toExclusiveUtc is not null)
        {
            query = query.Where(s => s.CreatedAt < toExclusiveUtc);
        }

        var rows = await query
            .Select(s => new StepRow(
                s.Id, s.WorkflowId, s.AgentName, s.ToolCallsJson, s.ValidationResult,
                s.DurationMs, s.Attempts, s.PromptTokens, s.CompletionTokens, s.CreatedAt))
            .ToListAsync(cancellationToken);

        // Agent runs only. A tool call is recorded under the same agent's name, and is not a
        // run of that agent — counting it would turn three tool calls into three more runs.
        var runs = rows.Where(r => AgentAnalysis.IsAgentRunStep(r.ToolCallsJson)).ToList();

        var perAgent = AgentNames
            .Select(name => Row(name, runs.Where(r => r.AgentName == name).ToList()))
            .ToList();

        return new AgentMetricsDto(
            fromDate,
            toDate,
            new AgentPricingDto(
                _pricing.IsConfigured,
                _pricing.InputPricePerMillionTokensUsd,
                _pricing.OutputPricePerMillionTokensUsd),
            Row(null, runs),
            perAgent,
            Daily(runs, fromDate, toDate),
            SlowestRuns(runs),
            CostliestRuns(runs));
    }

    // -----------------------------------------------------------------------
    // One agent (or all of them)
    // -----------------------------------------------------------------------

    private AgentMetricsRowDto Row(string? agentName, IReadOnlyList<StepRow> runs)
    {
        var succeeded = runs.Count(r => r.ValidationResult == Ok);
        var safeFailures = runs.Count(r => r.ValidationResult == SafeFailure);
        var callFailures = runs.Count(r => r.ValidationResult == CallFailed);
        var rejected = runs.Count(r => r.ValidationResult == Rejected);
        var failed = safeFailures + callFailures + rejected;

        var withAttempts = runs.Where(r => r.Attempts is not null).ToList();
        var retried = withAttempts.Count(r => r.Attempts > 1);

        var timed = Timed(runs).Select(r => r.DurationMs).ToList();

        var withUsage = runs.Where(HasUsage).ToList();
        long? prompt = withUsage.Count == 0 ? null : withUsage.Sum(r => (long)r.PromptTokens!.Value);
        long? completion = withUsage.Count == 0 ? null : withUsage.Sum(r => (long)r.CompletionTokens!.Value);

        return new AgentMetricsRowDto(
            agentName,
            Runs: runs.Count,
            Succeeded: succeeded,
            SafeFailures: safeFailures,
            CallFailures: callFailures,
            Rejected: rejected,
            Failed: failed,
            FailureRate: MetricRules.Percent(failed, runs.Count),
            RunsReportingAttempts: withAttempts.Count,
            RetriedRuns: retried,
            RetryRate: MetricRules.Percent(retried, withAttempts.Count),
            TimedRuns: timed.Count,
            MedianDurationMs: MetricRules.MedianMs(timed),
            P95DurationMs: MetricRules.PercentileMs(timed, 95),
            MaxDurationMs: timed.Count == 0 ? null : timed.Max(),
            RunsWithUsage: withUsage.Count,
            PromptTokens: prompt,
            CompletionTokens: completion,
            TotalTokens: prompt + completion,
            AverageTokensPerRun: withUsage.Count == 0
                ? null
                : (long)Math.Round((decimal)(prompt!.Value + completion!.Value) / withUsage.Count, MidpointRounding.AwayFromZero),
            EstimatedCostUsd: withUsage.Count == 0 ? null : Money(_pricing.CostOf(prompt!.Value, completion!.Value)));
    }

    // -----------------------------------------------------------------------
    // The daily series
    // -----------------------------------------------------------------------

    private IReadOnlyList<AgentMetricsDayDto> Daily(IReadOnlyList<StepRow> runs, DateOnly? fromDate, DateOnly? toDate)
    {
        var lastDay = toDate ?? DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var firstDay = lastDay.AddDays(-(DailyDays - 1));

        if (fromDate is not null && fromDate > firstDay)
        {
            firstDay = fromDate.Value;
        }

        var byDay = runs
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt))
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<AgentMetricsDayDto>();

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var dayRuns = byDay.GetValueOrDefault(day) ?? [];
            var withUsage = dayRuns.Where(HasUsage).ToList();

            long? prompt;
            long? completion;

            if (withUsage.Count > 0)
            {
                prompt = withUsage.Sum(r => (long)r.PromptTokens!.Value);
                completion = withUsage.Sum(r => (long)r.CompletionTokens!.Value);
            }
            else
            {
                // No runs: nothing was spent, a real 0. Runs that reported nothing: unknown.
                prompt = dayRuns.Count == 0 ? 0 : null;
                completion = dayRuns.Count == 0 ? 0 : null;
            }

            days.Add(new AgentMetricsDayDto(
                day,
                dayRuns.Count,
                withUsage.Count,
                prompt,
                completion,
                prompt is null ? null : Money(_pricing.CostOf(prompt.Value, completion!.Value))));
        }

        return days;
    }

    // -----------------------------------------------------------------------
    // The run lists
    // -----------------------------------------------------------------------

    private IReadOnlyList<AgentRunSummaryDto> SlowestRuns(IReadOnlyList<StepRow> runs) =>
        Timed(runs)
            .OrderByDescending(r => r.DurationMs)
            .ThenBy(r => r.Id)
            .Take(RunListSize)
            .Select(Summary)
            .ToList();

    private IReadOnlyList<AgentRunSummaryDto> CostliestRuns(IReadOnlyList<StepRow> runs) =>
        runs.Where(HasUsage)
            .OrderByDescending(r => (long)r.PromptTokens!.Value + r.CompletionTokens!.Value)
            .ThenBy(r => r.Id)
            .Take(RunListSize)
            .Select(Summary)
            .ToList();

    private AgentRunSummaryDto Summary(StepRow r) => new(
        r.Id,
        r.WorkflowId,
        r.AgentName,
        r.ValidationResult,
        r.DurationMs,
        r.Attempts,
        r.PromptTokens,
        r.CompletionTokens,
        HasUsage(r) ? Money(_pricing.CostOf(r.PromptTokens!.Value, r.CompletionTokens!.Value)) : null,
        r.CreatedAt);

    // -----------------------------------------------------------------------
    // Shared rules
    // -----------------------------------------------------------------------

    /// <summary>
    /// The runs whose duration is the agent's own: it returned (a CallFailed step's time is
    /// the call's, usually the timeout) and it has one (an older reply's later agents were
    /// recorded as 0 — "timed with the run").
    /// </summary>
    private static IEnumerable<StepRow> Timed(IEnumerable<StepRow> runs) =>
        runs.Where(r => r.ValidationResult != CallFailed && r.DurationMs > 0);

    private static bool HasUsage(StepRow r) => r.PromptTokens is not null && r.CompletionTokens is not null;

    /// <summary>
    /// US dollars to six decimal places — a single agent run costs fractions of a cent, so
    /// two places would show nearly every run as $0.00. Rounded once, after summing.
    /// </summary>
    private static decimal? Money(decimal? usd) =>
        usd is null ? null : Math.Round(usd.Value, 6, MidpointRounding.AwayFromZero);

    private sealed record StepRow(
        int Id,
        int WorkflowId,
        string AgentName,
        string? ToolCallsJson,
        string? ValidationResult,
        int DurationMs,
        int? Attempts,
        int? PromptTokens,
        int? CompletionTokens,
        DateTime CreatedAt);
}
