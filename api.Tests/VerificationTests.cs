using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// The verification loop's rules, exercised through IVerificationService against a real
/// database. What is pinned here is the handful of decisions that are easy to get subtly
/// wrong and impossible to notice afterwards:
///
///   * The DELAY is applied from completion, not from now.
///   * A check is answered ONCE. There is no second round.
///   * The confirmation rate excludes checks nobody answered — counting silence as
///     agreement would make the one number this component exists to produce flattering
///     and wrong.
/// </summary>
public class VerificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public VerificationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateForCompletedWorkOrder_SetsDueAtFromCompletionPlusTheConfiguredDelay()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();
        var settings = scope.ServiceProvider.GetRequiredService<VerificationSettings>();

        var completedAt = DateTime.UtcNow.AddDays(-3);
        var order = await SeedCompletedWorkOrderAsync(db, "DUE", completedAt);

        var check = await service.CreateForCompletedWorkOrderAsync(order.Id);

        Assert.NotNull(check);
        Assert.Equal(VerificationStatus.Pending, check!.Status);
        Assert.Null(check.ReporterConfirmed);

        // Measured from COMPLETION, not from now — a check raised late still falls due
        // when it should have.
        Assert.Equal(completedAt.AddDays(settings.DelayDays), check.DueAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateForCompletedWorkOrder_RefusesWorkThatIsNotFinished()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();

        var order = await SeedCompletedWorkOrderAsync(db, "LIVE", DateTime.UtcNow.AddDays(-9));
        order.Status = WorkOrderStatus.InProgress;
        order.CompletedAt = null;
        await db.SaveChangesAsync();

        Assert.Null(await service.CreateForCompletedWorkOrderAsync(order.Id));
        Assert.Null(await service.CreateForCompletedWorkOrderAsync(order.Id + 10_000));
    }

    [Fact]
    public async Task Sweep_PicksUpOnlyChecksWhoseDueDateHasPassed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();

        var overdue = await SeedCheckAsync(db, "SWPA", DateTime.UtcNow.AddDays(-2), VerificationStatus.Pending);
        var notYetDue = await SeedCheckAsync(db, "SWPB", DateTime.UtcNow.AddDays(5), VerificationStatus.Pending);

        var result = await service.ProcessDueChecksAsync();
        Assert.True(result.AskedReporter >= 1);

        db.ChangeTracker.Clear();

        var overdueAfter = await db.VerificationChecks.SingleAsync(v => v.Id == overdue.Id);
        Assert.Equal(VerificationStatus.AwaitingReporterResponse, overdueAfter.Status);
        Assert.NotNull(overdueAfter.ProcessedAt);

        // Still waiting out its delay, and the sweep must leave it alone.
        var pendingAfter = await db.VerificationChecks.SingleAsync(v => v.Id == notYetDue.Id);
        Assert.Equal(VerificationStatus.Pending, pendingAfter.Status);
        Assert.Null(pendingAfter.ProcessedAt);
    }

    [Fact]
    public async Task ReporterResponse_SetsStatusAndAnswerTogether_AndIsRefusedTwice()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();

        var check = await SeedCheckAsync(
            db, "ANS", DateTime.UtcNow.AddDays(-1), VerificationStatus.AwaitingReporterResponse);

        var reporterId = await ReporterOfAsync(db, check);

        var accepted = await service.RecordReporterResponseAsync(
            check.Id, reporterId, new ReporterConfirmationDto(Confirmed: false, Comment: "Still cutting out."));

        Assert.Equal(ConfirmVerificationOutcome.Success, accepted);

        db.ChangeTracker.Clear();
        var answered = await db.VerificationChecks.SingleAsync(v => v.Id == check.Id);

        // Status and answer move in the same SaveChanges, so they cannot disagree.
        Assert.Equal(VerificationStatus.Reopened, answered.Status);
        Assert.False(answered.ReporterConfirmed);
        Assert.Equal("Still cutting out.", answered.ReporterComment);
        Assert.NotNull(answered.ReporterRespondedAt);

        // Handed to the agent with the answer, not left for the next sweep.
        Assert.Equal(answered.ReporterRespondedAt, answered.AgentQueuedAt);

        // ONE ANSWER. A second is refused rather than silently overwriting the first —
        // there is no unique index to lean on here, so the service has to say no itself.
        var second = await service.RecordReporterResponseAsync(
            check.Id, reporterId, new ReporterConfirmationDto(Confirmed: true, Comment: "Changed my mind."));

        Assert.Equal(ConfirmVerificationOutcome.AlreadyAnswered, second);

        db.ChangeTracker.Clear();
        var unchanged = await db.VerificationChecks.SingleAsync(v => v.Id == check.Id);
        Assert.Equal(VerificationStatus.Reopened, unchanged.Status);
        Assert.False(unchanged.ReporterConfirmed);
    }

    [Fact]
    public async Task Metrics_ExcludeUnansweredChecksFromTheConfirmationRate()
    {
        // Its own factory, because this asserts on counts across the whole table and must
        // not see rows another test created.
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();

        await SeedCheckAsync(db, "MX1", DateTime.UtcNow.AddDays(-9), VerificationStatus.Confirmed, confirmed: true);
        await SeedCheckAsync(db, "MX2", DateTime.UtcNow.AddDays(-9), VerificationStatus.Confirmed, confirmed: true);
        await SeedCheckAsync(db, "MX3", DateTime.UtcNow.AddDays(-9), VerificationStatus.Confirmed, confirmed: true);
        await SeedCheckAsync(db, "MX4", DateTime.UtcNow.AddDays(-9), VerificationStatus.Reopened, confirmed: false);
        await SeedCheckAsync(db, "MX5", DateTime.UtcNow.AddDays(-9), VerificationStatus.Expired);
        await SeedCheckAsync(db, "MX6", DateTime.UtcNow.AddDays(-2), VerificationStatus.Pending);

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(6, metrics.Total);
        Assert.Equal(3, metrics.Confirmed);
        Assert.Equal(1, metrics.Reopened);
        Assert.Equal(1, metrics.Expired);

        // 3 of 4 ANSWERED checks, not 3 of 6. Counting the expired and pending ones would
        // report 50%, which understates a loop nobody failed to answer.
        Assert.Equal(75.00m, metrics.ConfirmationRate);
        Assert.Equal(25.00m, metrics.ReopenRate);

        // The Pending row is past its due date and the sweep has not run here.
        Assert.Equal(1, metrics.OverdueUnprocessed);
    }

    // ---------------------------------------------------------------------------

    private static Task<int> ReporterOfAsync(AppDbContext db, VerificationCheck check) =>
        db.WorkOrders
            .Where(w => w.Id == check.WorkOrderId)
            .Select(w => w.Report!.ReporterId)
            .SingleAsync();

    private static async Task<VerificationCheck> SeedCheckAsync(
        AppDbContext db,
        string prefix,
        DateTime dueAt,
        VerificationStatus status,
        bool? confirmed = null)
    {
        var order = await SeedCompletedWorkOrderAsync(db, prefix, dueAt.AddDays(-5));

        var check = new VerificationCheck
        {
            WorkOrderId = order.Id,
            AssetId = order.AssetId,
            DueAt = dueAt,
            Status = status,
            ReporterConfirmed = confirmed,
            ReporterRespondedAt = confirmed is null ? null : DateTime.UtcNow.AddDays(-1),
            ProcessedAt = confirmed is null ? null : DateTime.UtcNow.AddDays(-2)
        };

        db.VerificationChecks.Add(check);
        await db.SaveChangesAsync();
        return check;
    }

    /// <summary>A completed work order with its report, reporter, room and asset. Shared with VerificationSweepTests.</summary>
    internal static async Task<WorkOrder> SeedCompletedWorkOrderAsync(
        AppDbContext db, string prefix, DateTime completedAt)
    {
        var building = new Building { Name = $"Block {prefix}", Code = prefix };
        var room = new Room { Building = building, Name = "Lecture Hall", Code = $"{prefix}-1", Floor = 1 };
        var reporter = new User
        {
            Email = $"{prefix.ToLowerInvariant()}@example.com",
            PasswordHash = "not-a-real-hash",
            FullName = "Test Reporter",
            Role = Role.Reporter
        };
        var category = new AssetCategory { Name = $"Projector {prefix}" };
        var asset = new Asset
        {
            AssetTag = $"PRJ-{prefix}-01",
            Name = "Ceiling projector",
            Category = category,
            Room = room,
            InstalledOn = new DateOnly(2024, 1, 15)
        };
        var report = new Report
        {
            Reporter = reporter,
            Room = room,
            Asset = asset,
            Description = $"Projector fault reported for {prefix}."
        };
        var order = new WorkOrder
        {
            Report = report,
            Asset = asset,
            Status = WorkOrderStatus.Completed,
            Strategy = WorkOrderStrategy.SingleJob,
            EstimatedCost = 9_000m,
            ActualCost = 9_000m,
            ResolutionNote = "filter cleaned, tested 30min.",
            CompletedAt = completedAt
        };

        db.AddRange(building, room, reporter, category, asset, report, order);
        await db.SaveChangesAsync();
        return order;
    }
}
