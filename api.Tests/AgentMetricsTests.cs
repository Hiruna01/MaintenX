using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace api.Tests;

/// <summary>A FixedClockApiFactory with an LLM price configured: $0.30 in, $2.50 out per million tokens.</summary>
public class PricedApiFactory : FixedClockApiFactory
{
    public static readonly LlmPricingSettings Pricing = new()
    {
        InputPricePerMillionTokensUsd = 0.30m,
        OutputPricePerMillionTokensUsd = 2.50m
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LlmPricingSettings>();
            services.AddSingleton(Pricing);
        });
    }
}

/// <summary>
/// GET /api/analytics/agents — the agent monitoring read — through the real pipeline, with
/// the clock pinned to <see cref="FixedClockApiFactory.Today"/> (31 May 2026). Every test
/// builds its own factory: each figure is a count over the whole table. What is pinned:
///
///   * an empty database is zeros and nulls, never NaN and never a 500, with all five agents
///     listed and a 30-day series;
///   * FacilitiesManager and Admin only, 401 and 403 kept apart, a reversed range a 400;
///   * only AGENT-LEVEL steps are runs — not tool calls, not approval steps;
///   * failures, retries and latency are split the way the runners record them: a CallFailed
///     step's time and a 0 ms "timed with the run" step are not the agent's latency;
///   * tokens are what was REPORTED: runs without usage are counted but never costed, and a
///     figure with nothing to compute from is null, not 0; cost appears only with a price;
///   * the date range is inclusive at both ends, and the daily series tells a day with no
///     runs (0) from a day whose runs reported nothing (null).
/// </summary>
public class AgentMetricsTests
{
    private const string Url = "/api/analytics/agents";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] PipelineOrder = ["planner", "clarifier", "diagnostic", "strategist", "verification"];

    // Midday on the pinned day, so no UTC-day arithmetic can move a step onto another date.
    private static readonly DateTime OnToday = FixedClockApiFactory.Today.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

    [Fact]
    public async Task EmptyDatabase_IsZerosAndNulls_NotNaN_WithEveryAgentListed()
    {
        using var factory = new FixedClockApiFactory();
        var manager = await ClientForAsync(factory, Role.FacilitiesManager);

        var response = await manager.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("NaN", await response.Content.ReadAsStringAsync());

        var metrics = (await response.Content.ReadFromJsonAsync<AgentMetricsDto>(JsonOptions))!;

        Assert.False(metrics.Pricing.Configured);
        Assert.Null(metrics.Totals.AgentName);
        Assert.Equal(0, metrics.Totals.Runs);
        Assert.Equal(0m, metrics.Totals.FailureRate);
        Assert.Equal(0m, metrics.Totals.RetryRate);

        // Null is not zero: nothing timed has no median, and nothing reported has no tokens.
        Assert.Null(metrics.Totals.MedianDurationMs);
        Assert.Null(metrics.Totals.P95DurationMs);
        Assert.Null(metrics.Totals.PromptTokens);
        Assert.Null(metrics.Totals.TotalTokens);
        Assert.Null(metrics.Totals.AverageTokensPerRun);
        Assert.Null(metrics.Totals.EstimatedCostUsd);

        Assert.Equal(PipelineOrder, metrics.Agents.Select(a => a.AgentName));
        Assert.All(metrics.Agents, a => Assert.Equal(0, a.Runs));

        // Thirty days ending today, every one present. No runs is a real 0 tokens — nothing
        // was spent — but with no price configured there is still no cost.
        Assert.Equal(AgentMetricsService.DailyDays, metrics.Daily.Count);
        Assert.Equal(FixedClockApiFactory.Today, metrics.Daily[^1].Date);
        Assert.Equal(FixedClockApiFactory.Today.AddDays(-29), metrics.Daily[0].Date);
        Assert.All(metrics.Daily, d =>
        {
            Assert.Equal(0, d.Runs);
            Assert.Equal(0L, d.PromptTokens);
            Assert.Null(d.EstimatedCostUsd);
        });

        Assert.Empty(metrics.SlowestRuns);
        Assert.Empty(metrics.CostliestRuns);
    }

    [Fact]
    public async Task AgentMetrics_AreForAManagerAndAnAdmin_401And403KeptApart()
    {
        using var factory = new FixedClockApiFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url)).StatusCode);

        foreach (var role in new[] { Role.Reporter, Role.Technician })
        {
            var client = await ClientForAsync(factory, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        }

        foreach (var role in new[] { Role.FacilitiesManager, Role.Admin })
        {
            var client = await ClientForAsync(factory, role);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        }

        var manager = await ClientForAsync(factory, Role.FacilitiesManager);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync($"{Url}?fromDate=2026-05-10&toDate=2026-05-09")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"{Url}?fromDate=2026-05-10&toDate=2026-05-10")).StatusCode);
    }

    [Fact]
    public async Task RunsFailuresRetriesLatencyAndTokens_AreCountedPerAgent_FromAgentLevelStepsOnly()
    {
        using var factory = new PricedApiFactory();
        await SeedRunsAsync(factory);

        var metrics = await GetAsync(await ClientForAsync(factory, Role.FacilitiesManager), Url);
        var totals = metrics.Totals;

        // Seven agent runs. The tool call and the approval step are rows, not runs.
        Assert.Equal(7, totals.Runs);
        Assert.Equal((4, 1, 1, 1), (totals.Succeeded, totals.SafeFailures, totals.CallFailures, totals.Rejected));
        Assert.Equal((3, 42.86m), (totals.Failed, totals.FailureRate));

        // Two retries among the six runs that reported attempts (the failed call reported none).
        Assert.Equal((6, 2, 33.33m), (totals.RunsReportingAttempts, totals.RetriedRuns, totals.RetryRate));

        // Latency over five timed runs — 200, 300, 500, 900, 1200. Not the CallFailed step's
        // 360 s (that is the call's timeout) and not the strategist's 0 ("timed with the run").
        Assert.Equal(5, totals.TimedRuns);
        Assert.Equal((500, 1200, 1200), (totals.MedianDurationMs!.Value, totals.P95DurationMs!.Value, totals.MaxDurationMs!.Value));

        // Tokens over the five runs that reported them, costed at $0.30 / $2.50 per million.
        Assert.Equal(5, totals.RunsWithUsage);
        Assert.Equal((4300L, 450L, 4750L), (totals.PromptTokens!.Value, totals.CompletionTokens!.Value, totals.TotalTokens!.Value));
        Assert.Equal(950L, totals.AverageTokensPerRun); // 4750 / 5
        Assert.Equal(0.002415m, totals.EstimatedCostUsd); // 0.00129 + 0.001125

        var byAgent = metrics.Agents.ToDictionary(a => a.AgentName!);

        Assert.Equal(PipelineOrder, metrics.Agents.Select(a => a.AgentName));
        Assert.Equal((2, 1, 1), (byAgent["planner"].Runs, byAgent["planner"].Succeeded, byAgent["planner"].Rejected));
        Assert.Equal(0.00028m, byAgent["clarifier"].EstimatedCostUsd); // 600 in, 40 out
        Assert.Equal(100m, byAgent["clarifier"].RetryRate);

        // Two diagnostic runs, both failed, neither reported tokens: counted, never costed.
        var diagnostic = byAgent["diagnostic"];
        Assert.Equal((2, 100m), (diagnostic.Runs, diagnostic.FailureRate));
        Assert.Equal((1, 900), (diagnostic.TimedRuns, diagnostic.MedianDurationMs!.Value));
        Assert.Equal(0, diagnostic.RunsWithUsage);
        Assert.Null(diagnostic.PromptTokens);
        Assert.Null(diagnostic.EstimatedCostUsd);

        // The strategist reported tokens but no time of its own.
        Assert.Equal((0, (int?)null, 1), (byAgent["strategist"].TimedRuns, byAgent["strategist"].MedianDurationMs, byAgent["strategist"].RunsWithUsage));

        // Slowest first, timed runs only; most tokens first, reporting runs only.
        Assert.Equal(new[] { 1200, 900, 500, 300, 200 }, metrics.SlowestRuns.Select(r => r.DurationMs));
        Assert.Equal(
            new[] { "verification", "strategist", "clarifier", "planner", "planner" },
            metrics.CostliestRuns.Select(r => r.AgentName));
        Assert.Equal(0.00085m, metrics.CostliestRuns[0].EstimatedCostUsd); // 2000 in, 100 out

        // Every run today, none the day before: today's tokens, and a real $0 yesterday.
        var today = metrics.Daily[^1];
        Assert.Equal((7, 5, 4300L, 0.002415m), (today.Runs, today.RunsWithUsage, today.PromptTokens!.Value, today.EstimatedCostUsd!.Value));
        Assert.Equal((0, 0L, 0m), (metrics.Daily[^2].Runs, metrics.Daily[^2].PromptTokens!.Value, metrics.Daily[^2].EstimatedCostUsd!.Value));
    }

    [Fact]
    public async Task WithoutAPrice_TokensAreReported_ButNothingIsCosted()
    {
        using var factory = new FixedClockApiFactory();
        await SeedRunsAsync(factory);

        var metrics = await GetAsync(await ClientForAsync(factory, Role.Admin), Url);

        Assert.False(metrics.Pricing.Configured);
        Assert.Equal(4300L, metrics.Totals.PromptTokens);
        Assert.Null(metrics.Totals.EstimatedCostUsd);
        Assert.All(metrics.Agents, a => Assert.Null(a.EstimatedCostUsd));
        Assert.All(metrics.CostliestRuns, r => Assert.Null(r.EstimatedCostUsd));
        Assert.All(metrics.Daily, d => Assert.Null(d.EstimatedCostUsd));
    }

    [Fact]
    public async Task TheRange_IsInclusiveAtBothEnds_AndADayWhoseRunsReportedNothing_IsNullNotZero()
    {
        using var factory = new PricedApiFactory();
        int workflowId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            workflowId = await AddWorkflowAsync(db);

            await AddStepAsync(db, workflowId, "planner", "Ok", 300, 1, (100, 10), Utc(2026, 5, 9, 23, 59));
            await AddStepAsync(db, workflowId, "planner", "Ok", 300, 1, (200, 20), Utc(2026, 5, 10, 0, 0));
            await AddStepAsync(db, workflowId, "clarifier", "Ok", 400, 1, null, Utc(2026, 5, 11, 23, 59));
            await AddStepAsync(db, workflowId, "clarifier", "Ok", 400, 1, (300, 30), Utc(2026, 5, 12, 0, 0));
        }

        var manager = await ClientForAsync(factory, Role.FacilitiesManager);
        var metrics = await GetAsync(manager, $"{Url}?fromDate=2026-05-10&toDate=2026-05-11");

        // The 10th from its first minute, the 11th to its last — not the 9th, not the 12th.
        Assert.Equal(2, metrics.Totals.Runs);
        Assert.Equal(200L, metrics.Totals.PromptTokens);

        // The series is clipped to the range: two days, and the 11th's run reported nothing.
        Assert.Equal(
            new[] { new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 11) },
            metrics.Daily.Select(d => d.Date));
        Assert.Equal((1, 1, 200L), (metrics.Daily[0].Runs, metrics.Daily[0].RunsWithUsage, metrics.Daily[0].PromptTokens!.Value));
        Assert.Equal((1, 0), (metrics.Daily[1].Runs, metrics.Daily[1].RunsWithUsage));
        Assert.Null(metrics.Daily[1].PromptTokens);
        Assert.Null(metrics.Daily[1].EstimatedCostUsd);
    }

    // ---------------------------------------------------------------------------------
    // The latency arithmetic, as pure functions
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Median_IsTheMiddleValue_OrTheMiddleTwoAveraged_AndNullForNothing()
    {
        Assert.Null(MetricRules.MedianMs([]));
        Assert.Equal(700, MetricRules.MedianMs([700]));
        Assert.Equal(500, MetricRules.MedianMs([900, 200, 500]));
        Assert.Equal(400, MetricRules.MedianMs([200, 300, 500, 900]));
        Assert.Equal(2, MetricRules.MedianMs([1, 2])); // 1.5 rounds away from zero
    }

    [Fact]
    public void P95_IsTheNearestRank_AlwaysAnObservedValue()
    {
        Assert.Null(MetricRules.PercentileMs([], 95));
        Assert.Equal(40, MetricRules.PercentileMs([40], 95));

        // Twenty values 1..20: rank ceil(0.95 × 20) = 19.
        var twenty = Enumerable.Range(1, 20).ToList();
        Assert.Equal(19, MetricRules.PercentileMs(twenty, 95));

        // Twenty-one values: rank ceil(19.95) = 20 — one more value moves it up a rank.
        Assert.Equal(20, MetricRules.PercentileMs(Enumerable.Range(1, 21).ToList(), 95));
        Assert.Equal(10, MetricRules.PercentileMs(twenty, 50));
    }

    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Seven agent runs on one workflow, plus a tool call and an approval step that must not
    /// count — the shapes the runners and the tool router really write:
    ///
    ///   planner     Ok          300 ms  1 attempt   400 / 60 tokens
    ///   planner     Rejected    200 ms  1 attempt   300 / 50
    ///   clarifier   Ok          500 ms  2 attempts  600 / 40
    ///   diagnostic  SafeFailure 900 ms  2 attempts  —
    ///   diagnostic  CallFailed  360 s   —           —
    ///   strategist  Ok          0 ms    1 attempt   1000 / 200
    ///   verification Ok         1200 ms 1 attempt   2000 / 100
    /// </summary>
    private static async Task SeedRunsAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workflowId = await AddWorkflowAsync(db);

        await AddStepAsync(db, workflowId, "planner", "Ok", 300, 1, (400, 60), OnToday);
        await AddStepAsync(db, workflowId, "planner", "Rejected", 200, 1, (300, 50), OnToday);
        await AddStepAsync(db, workflowId, "clarifier", "Ok", 500, 2, (600, 40), OnToday);
        await AddStepAsync(db, workflowId, "diagnostic", "SafeFailure", 900, 2, null, OnToday);
        await AddStepAsync(db, workflowId, "diagnostic", "CallFailed", 360_000, null, null, OnToday);
        await AddStepAsync(db, workflowId, "strategist", "Ok", 0, 1, (1000, 200), OnToday);
        await AddStepAsync(db, workflowId, "verification", "Ok", 1200, 1, (2000, 100), OnToday);

        // A tool call under an agent's own name, and an approval step: rows, not agent runs.
        await AddStepAsync(db, workflowId, "clarifier", "Ok", 40, null, null, OnToday,
            toolCallsJson: """[{"tool":"get_room","id":3}]""");
        await AddStepAsync(db, workflowId, "approval", "ApprovalRequired", 0, null, null, OnToday, toolCallsJson: null);
    }

    private static async Task<int> AddWorkflowAsync(AppDbContext db)
    {
        var workflow = new AgentWorkflow { Objective = "Projector cuts out.", CurrentState = WorkflowState.Strategizing };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();
        return workflow.Id;
    }

    /// <summary>
    /// One step, then its CreatedAt set as DATA — AppDbContext stamps the real clock on insert,
    /// and these tests need the step on a chosen day.
    /// </summary>
    private static async Task AddStepAsync(
        AppDbContext db,
        int workflowId,
        string agentName,
        string validationResult,
        int durationMs,
        int? attempts,
        (int Prompt, int Completion)? usage,
        DateTime createdAt,
        string? toolCallsJson = "[]")
    {
        var step = new AgentStep
        {
            WorkflowId = workflowId,
            AgentName = agentName,
            ToolCallsJson = toolCallsJson,
            ValidationResult = validationResult,
            DurationMs = durationMs,
            Attempts = attempts,
            PromptTokens = usage?.Prompt,
            CompletionTokens = usage?.Completion
        };
        db.AgentSteps.Add(step);
        await db.SaveChangesAsync();

        await db.AgentSteps.Where(s => s.Id == step.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, createdAt));
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task<AgentMetricsDto> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AgentMetricsDto>(JsonOptions))!;
    }

    private static async Task<HttpClient> ClientForAsync(ApiFactory factory, Role role)
    {
        var client = factory.CreateClient();

        var response = await factory.RegisterAsync(
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "MetricsPass1", "Test User", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }
}
