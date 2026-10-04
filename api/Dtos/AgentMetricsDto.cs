namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — GET /api/analytics/agents. How the five agents themselves are doing:
/// how often each one runs, fails and retries, how long it takes, and how many tokens it
/// spends. The lecture's "metrics per agent", read off the AgentStep rows the runners already
/// write — no agent is called to produce it, and no model output is read as a number.
///
/// Only AGENT-LEVEL steps count as runs: one row per agent per /run call, the runner's
/// "[]" rows. Tool calls (their own rows under the same agent names) and approval steps are
/// not agent runs and are left out.
///
/// NULL IS NOT ZERO, throughout. Tokens are what the provider REPORTED; a run that reported
/// none (STUB_MODE, an older step, a call that failed) is counted in Runs but not in
/// RunsWithUsage, and a figure with nothing to be computed from is null, never 0. Cost is an
/// ESTIMATE from the configured price, null when no price is configured.
///
/// Every rate is a percentage, 0 to 100, two decimals, 0 when there is nothing to divide by,
/// and travels with its counts — the same rule as MetricsDto.
/// </summary>
public record AgentMetricsDto(
    // The range asked for, echoed back; both null means all time. UTC calendar days, both
    // ends inclusive, on the step's CreatedAt.
    DateOnly? FromDate,
    DateOnly? ToDate,

    AgentPricingDto Pricing,

    // Every agent together. Its AgentName is null.
    AgentMetricsRowDto Totals,

    // One row per agent, always all five, in pipeline order — an agent that never ran in
    // the range is a row of zeros and nulls, not a missing row.
    IReadOnlyList<AgentMetricsRowDto> Agents,

    // The daily series: the 30 UTC days ending on ToDate (or today), starting no earlier
    // than FromDate. Every day is present, so a chart has no silent gaps.
    IReadOnlyList<AgentMetricsDayDto> Daily,

    // The ten slowest timed runs, slowest first, and the ten that reported the most tokens.
    // Each links to its workflow, whose audit trail is the full trace.
    IReadOnlyList<AgentRunSummaryDto> SlowestRuns,
    IReadOnlyList<AgentRunSummaryDto> CostliestRuns);

/// <summary>
/// The price the cost estimate was made with, echoed so the page can say so. Not configured
/// means no cost anywhere in the response.
/// </summary>
public record AgentPricingDto(
    bool Configured,
    decimal? InputPricePerMillionTokensUsd,
    decimal? OutputPricePerMillionTokensUsd);

/// <summary>
/// One agent's figures — or every agent's, for the totals row (AgentName null).
/// </summary>
public record AgentMetricsRowDto(
    string? AgentName,

    // Agent-level steps, split by the ValidationResult the runner wrote. Ok + SafeFailures +
    // CallFailures + Rejected = Runs. Rejected is the planner's plan refused by PlanRules.
    int Runs,
    int Succeeded,
    int SafeFailures,
    int CallFailures,
    int Rejected,

    // SafeFailures + CallFailures + Rejected, and that as a share of Runs.
    int Failed,
    decimal FailureRate,

    // Runs that took the one LLM retry, out of the runs that reported their attempts.
    int RunsReportingAttempts,
    int RetriedRuns,
    decimal RetryRate,

    // Latency over the TIMED runs: the agent returned (not CallFailed — that time is the
    // call's, often the timeout) and has a duration of its own (an older reply's later agents
    // were recorded as 0, "timed with the run"). Null when none were timed.
    int TimedRuns,
    int? MedianDurationMs,
    int? P95DurationMs,
    int? MaxDurationMs,

    // Tokens over the runs that REPORTED usage. Null when none did.
    int RunsWithUsage,
    long? PromptTokens,
    long? CompletionTokens,
    long? TotalTokens,
    long? AverageTokensPerRun,

    // The estimate for those same runs; null when no price is configured or none reported.
    decimal? EstimatedCostUsd);

/// <summary>
/// One UTC day. A day with no runs spent nothing, so its tokens and cost are 0; a day with
/// runs that reported no usage is null — unknown, not free.
/// </summary>
public record AgentMetricsDayDto(
    DateOnly Date,
    int Runs,
    int RunsWithUsage,
    long? PromptTokens,
    long? CompletionTokens,
    decimal? EstimatedCostUsd);

/// <summary>One agent run, for the slowest and costliest lists.</summary>
public record AgentRunSummaryDto(
    int StepId,
    int WorkflowId,
    string AgentName,
    string? ValidationResult,
    int DurationMs,
    int? Attempts,
    int? PromptTokens,
    int? CompletionTokens,
    decimal? EstimatedCostUsd,
    DateTime CreatedAt);
