using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// GET /api/analytics/metrics, through the real pipeline with the clock pinned to
/// <see cref="FixedClockApiFactory.Today"/> (31 May 2026).
///
/// Every test builds its OWN factory, because every figure is a count across the whole
/// table and must not see rows another test created. What is pinned:
///
///   * an empty database is zeros, never NaN and never a 500 — and a median of nothing is
///     null, not 0 — on the verification read beside it as well;
///   * FacilitiesManager and Admin only, 401 and 403 kept apart, a reversed range a 400;
///   * the reopen rate counts ANSWERED checks only, per category and per month, with the
///     date range inclusive at both ends;
///   * the clarifier is counted as having run only on a successful agent-run step — not a
///     failed run, not one of its tool calls;
///   * the repeat-failure list reads FailureRules' 90-day window to the day, costs visits
///     from their work orders, and ranks by cost.
/// </summary>
public class AnalyticsTests
{
    private const string MetricsUrl = "/api/analytics/metrics";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task EmptyDatabase_IsZeros_NotNaN_AndNotA500()
    {
        using var factory = new FixedClockApiFactory();
        var (manager, _) = await ClientForAsync(factory, Role.FacilitiesManager);

        var response = await manager.GetAsync(MetricsUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // NaN would not even serialise; read the raw text too, so a "NaN" string would fail.
        Assert.DoesNotContain("NaN", await response.Content.ReadAsStringAsync());

        var metrics = (await response.Content.ReadFromJsonAsync<MetricsDto>(JsonOptions))!;

        Assert.Equal(0, metrics.ReopenRate.Answered);
        Assert.Equal(0m, metrics.ReopenRate.ReopenRate);
        Assert.Empty(metrics.ReopenRate.ByCategory);
        Assert.Equal(6, metrics.ReopenRate.MonthlyTrend.Count);
        Assert.All(metrics.ReopenRate.MonthlyTrend, m => Assert.Equal(0m, m.ReopenRate));

        Assert.Equal(0, metrics.Clarification.ReportsClarified);
        Assert.Equal(0m, metrics.Clarification.NoQuestionRate);
        Assert.Equal(0m, metrics.Clarification.AverageQuestionsPerReport);
        Assert.Equal(0m, metrics.Clarification.AnswerRate);
        // Null is not zero: nothing answered has no median, and 0 hours would claim one.
        Assert.Null(metrics.Clarification.MedianHoursToAnswer);

        Assert.Empty(metrics.RepeatFailures);
        Assert.Equal(FixedClockApiFactory.Today, metrics.RepeatFailuresAsOf);

        // The verification read beside it divides the same way over the same nothing.
        var verificationResponse = await manager.GetAsync("/api/analytics/verification");
        Assert.Equal(HttpStatusCode.OK, verificationResponse.StatusCode);
        Assert.DoesNotContain("NaN", await verificationResponse.Content.ReadAsStringAsync());

        var verification = (await verificationResponse.Content.ReadFromJsonAsync<VerificationMetricsDto>(JsonOptions))!;
        Assert.Equal(0, verification.Total);
        Assert.Equal(0m, verification.ConfirmationRate);
        Assert.Equal(0m, verification.ReopenRate);
        // An average of nothing is null, not 0 days.
        Assert.Null(verification.AverageDaysToRespond);
        Assert.Equal(0, verification.OverdueUnprocessed);
    }

    [Fact]
    public async Task Metrics_AreForAManagerAndAnAdmin_401And403KeptApart()
    {
        using var factory = new FixedClockApiFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(MetricsUrl)).StatusCode);

        foreach (var role in new[] { Role.Reporter, Role.Technician })
        {
            var (client, _) = await ClientForAsync(factory, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(MetricsUrl)).StatusCode);
        }

        foreach (var role in new[] { Role.FacilitiesManager, Role.Admin })
        {
            var (client, _) = await ClientForAsync(factory, role);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MetricsUrl)).StatusCode);
        }

        // A range that ends before it starts would be a page of zeros that looks like data.
        var (manager, _) = await ClientForAsync(factory, Role.FacilitiesManager);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync($"{MetricsUrl}?fromDate=2026-05-10&toDate=2026-05-09")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"{MetricsUrl}?fromDate=2026-05-10&toDate=2026-05-10")).StatusCode);
    }

    [Fact]
    public async Task ReopenRate_CountsAnsweredChecksOnly_PerCategoryAndPerMonth()
    {
        using var factory = new FixedClockApiFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var projectors = new AssetCategory { Name = "Projectors" };
            var aircon = new AssetCategory { Name = "Air conditioners" };
            db.AddRange(projectors, aircon);
            await db.SaveChangesAsync();

            await SeedCheckAsync(db, "RA1", projectors, VerificationStatus.Confirmed, Utc(2026, 5, 10, 23, 59));
            await SeedCheckAsync(db, "RA2", projectors, VerificationStatus.Reopened, Utc(2026, 5, 11));
            await SeedCheckAsync(db, "RA3", projectors, VerificationStatus.Confirmed, Utc(2026, 4, 5));
            await SeedCheckAsync(db, "RB1", aircon, VerificationStatus.Reopened, Utc(2026, 3, 15));
            await SeedCheckAsync(db, "RC1", aircon, VerificationStatus.Confirmed, Utc(2025, 11, 30, 23, 59));

            // Nobody answered these two, so they are in neither column.
            await SeedCheckAsync(db, "RB2", aircon, VerificationStatus.Expired, Utc(2026, 5, 1));
            await SeedCheckAsync(db, "RB3", aircon, VerificationStatus.Pending, Utc(2026, 5, 30));
        }

        var (manager, _) = await ClientForAsync(factory, Role.FacilitiesManager);
        var all = (await GetMetricsAsync(manager, MetricsUrl)).ReopenRate;

        // 2 of 5 ANSWERED — not 2 of 7.
        Assert.Equal(5, all.Answered);
        Assert.Equal(2, all.Reopened);
        Assert.Equal(40.00m, all.ReopenRate);

        // Worst category first.
        Assert.Equal(new[] { "Air conditioners", "Projectors" }, all.ByCategory.Select(c => c.CategoryName));
        Assert.Equal((2, 1, 50.00m), (all.ByCategory[0].Answered, all.ByCategory[0].Reopened, all.ByCategory[0].ReopenRate));
        Assert.Equal((3, 1, 33.33m), (all.ByCategory[1].Answered, all.ByCategory[1].Reopened, all.ByCategory[1].ReopenRate));

        // Dec 2025 to May 2026, empty months present. RC1 is the last minute of November,
        // outside the trend though inside the overall figure.
        Assert.Equal(
            new[] { (2025, 12, 0, 0m), (2026, 1, 0, 0m), (2026, 2, 0, 0m), (2026, 3, 1, 100.00m), (2026, 4, 1, 0m), (2026, 5, 2, 50.00m) },
            all.MonthlyTrend.Select(m => (m.Year, m.Month, m.Answered, m.ReopenRate)));

        // Both ends inclusive: the first minute of the 5th and the last of the 10th are in,
        // the first minute of the 11th is out.
        var ranged = (await GetMetricsAsync(manager, $"{MetricsUrl}?fromDate=2026-04-05&toDate=2026-05-10")).ReopenRate;
        Assert.Equal(2, ranged.Answered);
        Assert.Equal(0, ranged.Reopened);
        Assert.Equal(0m, ranged.ReopenRate);

        // The trend ends where the range ends — May holds RA1 but not RA2 — and fromDate does
        // not trim it: March is still there.
        Assert.Equal(1, ranged.MonthlyTrend.Single(m => m.Month == 5).Answered);
        Assert.Equal(0m, ranged.MonthlyTrend.Single(m => m.Month == 5).ReopenRate);
        Assert.Equal(100.00m, ranged.MonthlyTrend.Single(m => m.Month == 3).ReopenRate);
    }

    [Fact]
    public async Task Clarification_CountsOnlyReportsTheClarifierRanOn_AndTakesTheMedian()
    {
        using var factory = new FixedClockApiFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var building = new Building { Name = "Block CLR", Code = "CLR" };
            var room = new Room { Building = building, Name = "Lab", Code = "CLR-1", Floor = 1 };
            var reporter = new User
            {
                Email = "clr@example.com",
                PasswordHash = "not-a-real-hash",
                FullName = "Test Reporter",
                Role = Role.Reporter
            };
            db.AddRange(building, room, reporter);
            await db.SaveChangesAsync();

            var filedOn = Utc(2026, 5, 10);
            var askedAt = Utc(2026, 5, 10, 9, 0);

            // Clear enough: the clarifier ran and asked nothing.
            await SeedReportAsync(db, room, reporter, filedOn, ClarifierRun("Ok"));

            // Two questions, one answered after 3 hours.
            await SeedReportAsync(db, room, reporter, filedOn, ClarifierRun("Ok"),
                (askedAt, 3), (askedAt, null));

            // Two questions, answered after 5 and 10 hours.
            await SeedReportAsync(db, room, reporter, filedOn, ClarifierRun("Ok"),
                (askedAt, 5), (askedAt, 10));

            // NOT clarified: a failed run asked nothing because it could not.
            await SeedReportAsync(db, room, reporter, filedOn, ClarifierRun("SafeFailure"));

            // NOT clarified: a clarifier TOOL CALL is not its run, even though it says Ok.
            await SeedReportAsync(db, room, reporter, filedOn, new AgentStep
            {
                AgentName = AgentRunResponse.ClarifierAgentName,
                ToolCallsJson = """[{"tool":"get_room"}]""",
                ValidationResult = "Ok"
            });

            // NOT clarified: another agent's run.
            await SeedReportAsync(db, room, reporter, filedOn, new AgentStep
            {
                AgentName = AgentRunResponse.DiagnosticAgentName,
                ToolCallsJson = "[]",
                ValidationResult = "Ok"
            });

            // Filed the day before the range below: counted only without one.
            await SeedReportAsync(db, room, reporter, Utc(2026, 4, 30, 23, 59), ClarifierRun("Ok"));
        }

        var (manager, _) = await ClientForAsync(factory, Role.FacilitiesManager);
        var all = (await GetMetricsAsync(manager, MetricsUrl)).Clarification;

        Assert.Equal(4, all.ReportsClarified);
        Assert.Equal(2, all.ReportsWithNoQuestions);
        Assert.Equal(50.00m, all.NoQuestionRate);

        Assert.Equal(2, all.ReportsWithQuestions);
        Assert.Equal(4, all.QuestionsAsked);
        Assert.Equal(2.00m, all.AverageQuestionsPerReport);

        Assert.Equal(3, all.QuestionsAnswered);
        Assert.Equal(75.00m, all.AnswerRate);

        // The middle of 3, 5 and 10 — not their mean, 6.
        Assert.Equal(5.0, all.MedianHoursToAnswer);

        var ranged = (await GetMetricsAsync(manager, $"{MetricsUrl}?fromDate=2026-05-01")).Clarification;
        Assert.Equal(3, ranged.ReportsClarified);
        Assert.Equal(1, ranged.ReportsWithNoQuestions);
        Assert.Equal(33.33m, ranged.NoQuestionRate);
    }

    [Fact]
    public async Task RepeatFailures_ReadTheNinetyDayWindowToTheDay_AndRankByCost()
    {
        // Today is 31 May 2026, so the window opens on 2 March: a visit on 2 March is day
        // 90 and counts, a visit on 1 March is day 91 and does not.
        using var factory = new FixedClockApiFactory();

        int projector, aircon, unwarranted;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Three visits, the first on day 90. Two came from work orders; one is seeded
            // history with no order and so no cost.
            var projectorOrder = await VerificationTests.SeedCompletedWorkOrderAsync(db, "RFP", Utc(2026, 4, 10));
            var projectorAsset = projectorOrder.Asset!;
            projectorAsset.WarrantyExpiresOn = new DateOnly(2026, 5, 31);   // expires today: still covered
            projectorOrder.ActualCost = 9_000m;
            var secondOrder = await VerificationTests.SeedCompletedWorkOrderAsync(db, "RFP2", Utc(2026, 5, 20));
            secondOrder.AssetId = projectorAsset.Id;
            secondOrder.ActualCost = 12_000.50m;
            AddVisit(db, projectorAsset.Id, new DateOnly(2026, 3, 2), workOrderId: null);
            AddVisit(db, projectorAsset.Id, new DateOnly(2026, 4, 10), projectorOrder.Id);
            AddVisit(db, projectorAsset.Id, new DateOnly(2026, 5, 20), secondOrder.Id);

            // Three visits, but the first is day 91: two inside the window, not a repeat.
            var airconAsset = (await VerificationTests.SeedCompletedWorkOrderAsync(db, "RFQ", Utc(2026, 5, 1))).Asset!;
            airconAsset.WarrantyExpiresOn = new DateOnly(2026, 5, 30);
            AddVisit(db, airconAsset.Id, new DateOnly(2026, 3, 1), workOrderId: null);
            AddVisit(db, airconAsset.Id, new DateOnly(2026, 4, 1), workOrderId: null);
            AddVisit(db, airconAsset.Id, new DateOnly(2026, 5, 1), workOrderId: null);

            // Three visits and the most money — first on the list. No warranty recorded.
            var costlyOrder = await VerificationTests.SeedCompletedWorkOrderAsync(db, "RFR", Utc(2026, 3, 30));
            costlyOrder.ActualCost = 30_000m;
            AddVisit(db, costlyOrder.AssetId, new DateOnly(2026, 3, 10), workOrderId: null);
            AddVisit(db, costlyOrder.AssetId, new DateOnly(2026, 3, 20), workOrderId: null);
            AddVisit(db, costlyOrder.AssetId, new DateOnly(2026, 3, 30), costlyOrder.Id);

            await db.SaveChangesAsync();
            (projector, aircon, unwarranted) = (projectorAsset.Id, airconAsset.Id, costlyOrder.AssetId);
        }

        var (manager, _) = await ClientForAsync(factory, Role.FacilitiesManager);
        var list = (await GetMetricsAsync(manager, MetricsUrl)).RepeatFailures;

        Assert.Equal(new[] { unwarranted, projector }, list.Select(r => r.AssetId));
        Assert.DoesNotContain(list, r => r.AssetId == aircon);

        var costly = list[0];
        Assert.Equal(3, costly.FailureCount);
        Assert.Equal(30_000m, costly.TotalCost);
        Assert.Equal(2, costly.VisitsWithoutCost);
        Assert.False(costly.IsUnderWarranty);
        Assert.Equal(new DateOnly(2026, 3, 30), costly.LastServicedOn);
        Assert.Equal(62, costly.DaysSinceLastService);

        var repeat = list[1];
        Assert.Equal(3, repeat.FailureCount);
        Assert.Equal(21_000.50m, repeat.TotalCost);
        Assert.Equal(1, repeat.VisitsWithoutCost);
        Assert.True(repeat.IsUnderWarranty);
        Assert.Equal(11, repeat.DaysSinceLastService);

        // With a toDate the window ends there instead: on 10 April the projector has two
        // visits in its window, and the costly asset is 11 days from its last one.
        var asOfApril = await GetMetricsAsync(manager, $"{MetricsUrl}?toDate=2026-04-10");
        Assert.Equal(new DateOnly(2026, 4, 10), asOfApril.RepeatFailuresAsOf);
        Assert.Equal(new[] { unwarranted }, asOfApril.RepeatFailures.Select(r => r.AssetId));
        Assert.Equal(11, asOfApril.RepeatFailures[0].DaysSinceLastService);
    }

    // ---------------------------------------------------------------------------

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task<MetricsDto> GetMetricsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MetricsDto>(JsonOptions))!;
    }

    /// <summary>A check on a completed work order whose asset is filed under <paramref name="category"/>.</summary>
    private static async Task SeedCheckAsync(
        AppDbContext db, string prefix, AssetCategory category, VerificationStatus status, DateTime dueAt)
    {
        var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, prefix, dueAt.AddDays(-5));
        order.Asset!.AssetCategoryId = category.Id;

        var answered = status is VerificationStatus.Confirmed or VerificationStatus.Reopened;
        db.VerificationChecks.Add(new VerificationCheck
        {
            WorkOrderId = order.Id,
            AssetId = order.AssetId,
            DueAt = dueAt,
            Status = status,
            ReporterConfirmed = answered ? status == VerificationStatus.Confirmed : null,
            ReporterRespondedAt = answered ? dueAt.AddDays(1) : null
        });
        await db.SaveChangesAsync();
    }

    private static AgentStep ClarifierRun(string validationResult) => new()
    {
        AgentName = AgentRunResponse.ClarifierAgentName,
        ToolCallsJson = "[]",
        ValidationResult = validationResult
    };

    /// <summary>
    /// A report filed at <paramref name="filedAt"/>, one workflow carrying
    /// <paramref name="step"/>, and one YesNo question per entry — asked at AskedAt and
    /// answered that many hours later, or never when the hours are null.
    ///
    /// The timestamps are written with ExecuteUpdate after the insert, because
    /// AppDbContext stamps CreatedAt on every Added row and would overwrite them.
    /// </summary>
    private static async Task SeedReportAsync(
        AppDbContext db,
        Room room,
        User reporter,
        DateTime filedAt,
        AgentStep step,
        params (DateTime AskedAt, int? AnsweredAfterHours)[] questions)
    {
        var report = new Report { ReporterId = reporter.Id, RoomId = room.Id, Description = "Projector flickers on and off." };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var workflow = new AgentWorkflow { ReportId = report.Id, Objective = report.Description, Steps = { step } };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        await db.Reports.Where(r => r.Id == report.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, filedAt));

        for (var i = 0; i < questions.Length; i++)
        {
            var (askedAt, hours) = questions[i];
            var question = new ClarificationQuestion
            {
                ReportId = report.Id,
                WorkflowId = workflow.Id,
                QuestionText = $"Question {i + 1}?",
                AnswerType = AnswerType.YesNo,
                DisplayOrder = i
            };

            if (hours is not null)
            {
                question.Answer = new ClarificationAnswer
                {
                    AnswerText = "Yes",
                    AnsweredByUserId = reporter.Id,
                    AnsweredAt = askedAt.AddHours(hours.Value)
                };
            }

            db.ClarificationQuestions.Add(question);
            await db.SaveChangesAsync();

            await db.ClarificationQuestions.Where(q => q.Id == question.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.CreatedAt, askedAt));
        }
    }

    private static void AddVisit(AppDbContext db, int assetId, DateOnly servicedOn, int? workOrderId) =>
        db.ServiceRecords.Add(new ServiceRecord
        {
            AssetId = assetId,
            ServicedOn = servicedOn,
            TechnicianName = "Test Technician",
            TechnicianNote = "fan noisy, cleaned.",
            Outcome = ServiceOutcome.TemporaryFix,
            WorkOrderId = workOrderId
        });

    private static async Task<(HttpClient Client, int UserId)> ClientForAsync(ApiFactory factory, Role role)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "MetricsPass1", "Test User", role),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }
}
