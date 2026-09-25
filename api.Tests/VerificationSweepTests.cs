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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace api.Tests;

/// <summary>
/// An ApiFactory whose clock the test sets, so the response window can be stood on to the
/// minute. The sweep reads "now" from TimeProvider for exactly this reason.
/// </summary>
public class SweepClockApiFactory : ApiFactory
{
    /// <summary>Thursday 1 October 2026, midday UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public MutableClock Clock { get; } = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>
/// The verification sweep — the pass VerificationSweepService runs on a timer and
/// POST /api/workflows/verification-sweep runs on demand.
///
/// Each test takes its OWN factory: a sweep acts on every due row in the table, so its
/// counts are only meaningful against rows the test itself created. What is pinned:
///
///   * only a Pending check whose DueAt has passed is asked — DueAt exactly now included —
///     and a check in any other state is left exactly as it was;
///   * a second pass over the same table, bad row and all, changes nothing;
///   * the response window, on its boundary, and that queueing never touches Status;
///   * a check is queued once, not once per pass;
///   * ONE BAD ROW DOES NOT STOP THE SWEEP — it is expired with a reason and the rest are
///     processed — and an answered check is never expired for a sweep failure;
///   * a Completed workflow moves to AwaitingVerification exactly when its delay is up, and
///     a second pass moves and creates nothing;
///   * the manual trigger is FacilitiesManager only, 401 and 403 kept apart.
/// </summary>
public class VerificationSweepTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Sweep_AsksOnlyPendingChecksWhoseDueAtHasPassed_AndLeavesEveryOtherStateAlone()
    {
        using var factory = new SweepClockApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();
        var now = factory.Clock.Now.UtcDateTime;

        // Due this instant: the delay is over, so it is asked. `<`, not `<=`, would miss it.
        var dueNow = await SeedAsync(db, "DNW", VerificationStatus.Pending, askedAt: null, dueAt: now);
        // A minute short of its delay: the fault has not had its time to come back.
        var aMinuteEarly = await SeedAsync(db, "AME", VerificationStatus.Pending, askedAt: null, dueAt: now.AddMinutes(1));

        // Past their DueAt, but NOT Pending — none of these may be asked again. Re-asking the
        // first would move ProcessedAt to now and quietly restart its response window, so a
        // silent reporter would never be handed on.
        var askedYesterday = await SeedAsync(db, "AYD", VerificationStatus.AwaitingReporterResponse, askedAt: now.AddDays(-1));
        var expired = await SeedAsync(db, "EXP", VerificationStatus.Expired, askedAt: now.AddDays(-10));
        var escalated = await SeedAsync(db, "ESC", VerificationStatus.Escalated, askedAt: now.AddDays(-10));
        var settled = await SeedAsync(db, "SET", VerificationStatus.Confirmed, askedAt: now.AddDays(-10),
            respondedAt: now.AddDays(-9), confirmed: true, queuedAt: now.AddDays(-9));

        var before = await SnapshotAsync(db);

        var result = await service.ProcessDueChecksAsync();

        Assert.Equal(new VerificationSweepResultDto(Processed: 1, WorkflowsAwaitingVerification: 0, AskedReporter: 1, QueuedForAgent: 0, Failed: 0), result);

        var after = await SnapshotAsync(db);

        Assert.Equal(VerificationStatus.AwaitingReporterResponse, after[dueNow].Status);
        Assert.Equal(now, after[dueNow].ProcessedAt!.Value, TimeSpan.FromSeconds(1));

        // Every other row is exactly as it was — status, both stamps and UpdatedAt, so a
        // write that changed nothing visible would still be caught.
        foreach (var id in new[] { aMinuteEarly, askedYesterday, expired, escalated, settled })
        {
            Assert.Equal(before[id], after[id]);
        }
    }

    [Fact]
    public async Task Sweep_RunTwice_TheSecondPassChangesNothing_BadRowIncluded()
    {
        using var factory = new SweepClockApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var window = TimeSpan.FromDays(scope.ServiceProvider.GetRequiredService<VerificationSettings>().ResponseWindowDays);
        var now = factory.Clock.Now.UtcDateTime;

        // One of every kind of work a pass does, plus a row that fails.
        await SeedAsync(db, "TW1", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-1));
        await SeedAsync(db, "TW2", VerificationStatus.AwaitingReporterResponse, askedAt: now - window - TimeSpan.FromHours(1));
        await SeedAsync(db, "TW3", VerificationStatus.Reopened, askedAt: now.AddDays(-2), respondedAt: now.AddDays(-1), confirmed: false);
        var bad = await SeedAsync(db, "TWB", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-2));

        // The same failing context for both passes: if the second pass touched the bad row
        // again it would fail again, and Failed would say so.
        using var failingDb = ContextWith(db, new FailWhen(c => c.Id == bad && c.Status == VerificationStatus.AwaitingReporterResponse));
        var service = new VerificationService(
            failingDb,
            scope.ServiceProvider.GetRequiredService<VerificationSettings>(),
            factory.Clock,
            scope.ServiceProvider.GetRequiredService<ILogger<VerificationService>>());

        var first = await service.ProcessDueChecksAsync();
        Assert.Equal(new VerificationSweepResultDto(Processed: 4, WorkflowsAwaitingVerification: 0, AskedReporter: 1, QueuedForAgent: 2, Failed: 1), first);

        var afterFirst = await SnapshotAsync(db);
        Assert.Equal(VerificationStatus.Expired, afterFirst[bad].Status);

        // The same instant, then a minute on — the timer and the button pressed together, or
        // the next tick. Neither may ask, queue or expire anything a second time.
        foreach (var step in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(1) })
        {
            factory.Clock.Now += step;

            var again = await service.ProcessDueChecksAsync();

            Assert.Equal(new VerificationSweepResultDto(Processed: 0, WorkflowsAwaitingVerification: 0, AskedReporter: 0, QueuedForAgent: 0, Failed: 0), again);
            Assert.Equal(afterFirst, await SnapshotAsync(db));
        }
    }

    [Fact]
    public async Task Sweep_QueuesAnsweredAndLongSilentChecks_OnTheWindowBoundary_WithoutTouchingStatus()
    {
        using var factory = new SweepClockApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IVerificationService>();
        var window = TimeSpan.FromDays(scope.ServiceProvider.GetRequiredService<VerificationSettings>().ResponseWindowDays);
        var now = factory.Clock.Now.UtcDateTime;

        // Asked exactly the window ago: silent long enough.
        var onTheLine = await SeedAsync(db, "ONL", VerificationStatus.AwaitingReporterResponse, askedAt: now - window);
        // A minute short of it: the reporter still has time.
        var justShort = await SeedAsync(db, "SHT", VerificationStatus.AwaitingReporterResponse, askedAt: now - window + TimeSpan.FromMinutes(1));
        // Answered yesterday, a day after being asked: ready now, whatever the window says.
        var answered = await SeedAsync(db, "ANS", VerificationStatus.Reopened, askedAt: now.AddDays(-2), respondedAt: now.AddDays(-1), confirmed: false);
        // Overdue and never asked: asked in this pass, and so NOT also queued in it.
        var neverAsked = await SeedAsync(db, "NEW", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-10));

        var result = await service.ProcessDueChecksAsync();

        Assert.Equal(new VerificationSweepResultDto(Processed: 3, WorkflowsAwaitingVerification: 0, AskedReporter: 1, QueuedForAgent: 2, Failed: 0), result);

        db.ChangeTracker.Clear();
        var rows = await db.VerificationChecks.ToDictionaryAsync(v => v.Id);

        Assert.Equal(now, rows[onTheLine].AgentQueuedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Null(rows[justShort].AgentQueuedAt);
        Assert.NotNull(rows[answered].AgentQueuedAt);
        Assert.Null(rows[neverAsked].AgentQueuedAt);

        // Queueing is not a verdict. The silent check is still open for a late answer, and
        // the answered one keeps what its answer made it.
        Assert.Equal(VerificationStatus.AwaitingReporterResponse, rows[onTheLine].Status);
        Assert.Equal(VerificationStatus.Reopened, rows[answered].Status);
        Assert.False(rows[answered].ReporterConfirmed);

        Assert.Equal(VerificationStatus.AwaitingReporterResponse, rows[neverAsked].Status);
        Assert.Equal(now, rows[neverAsked].ProcessedAt!.Value, TimeSpan.FromSeconds(1));

        // A second pass a minute later finds nothing to do: queued ONCE, not once per pass.
        // The check that was a minute short has now crossed the line.
        factory.Clock.Now += TimeSpan.FromMinutes(1);
        var second = await service.ProcessDueChecksAsync();

        Assert.Equal(new VerificationSweepResultDto(Processed: 1, WorkflowsAwaitingVerification: 0, AskedReporter: 0, QueuedForAgent: 1, Failed: 0), second);
    }

    [Fact]
    public async Task Sweep_OneBadRow_IsExpiredWithTheReason_AndTheRestAreStillProcessed()
    {
        using var factory = new SweepClockApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = factory.Clock.Now.UtcDateTime;

        var first = await SeedAsync(db, "OK1", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-3));
        var bad = await SeedAsync(db, "BAD", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-2));
        var last = await SeedAsync(db, "OK2", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-1));

        // The bad row sits BETWEEN two good ones, so a sweep that stopped at the first
        // failure would miss the last.
        var interceptor = new FailWhen(c => c.Id == bad && c.Status == VerificationStatus.AwaitingReporterResponse);
        using var failingDb = ContextWith(db, interceptor);
        var service = new VerificationService(
            failingDb,
            scope.ServiceProvider.GetRequiredService<VerificationSettings>(),
            factory.Clock,
            scope.ServiceProvider.GetRequiredService<ILogger<VerificationService>>());

        var result = await service.ProcessDueChecksAsync();

        Assert.Equal(new VerificationSweepResultDto(Processed: 3, WorkflowsAwaitingVerification: 0, AskedReporter: 2, QueuedForAgent: 0, Failed: 1), result);

        db.ChangeTracker.Clear();
        var rows = await db.VerificationChecks.ToDictionaryAsync(v => v.Id);

        Assert.Equal(VerificationStatus.AwaitingReporterResponse, rows[first].Status);
        Assert.Equal(VerificationStatus.AwaitingReporterResponse, rows[last].Status);

        Assert.Equal(VerificationStatus.Expired, rows[bad].Status);
        Assert.Contains("asking the reporter", rows[bad].ExpiredReason);
        Assert.Contains(FailWhen.Message, rows[bad].ExpiredReason);
        Assert.Null(rows[bad].ReporterConfirmed);

        Assert.Contains(factory.Logs.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains($"check {bad}"));
    }

    [Fact]
    public async Task Sweep_FailingOnAnAnsweredCheck_LeavesTheAnswerAlone_RatherThanExpiringIt()
    {
        using var factory = new SweepClockApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = factory.Clock.Now.UtcDateTime;

        var answered = await SeedAsync(db, "CNF", VerificationStatus.Confirmed, askedAt: now.AddDays(-2), respondedAt: now.AddDays(-1), confirmed: true);
        // Queued after the bad one. Nothing expires the bad row here, so its failed change
        // stays tracked unless the sweep clears it — and would then fail this row's save too.
        var next = await SeedAsync(db, "NXT", VerificationStatus.Confirmed, askedAt: now.AddDays(-2), respondedAt: now.AddDays(-1), confirmed: true);

        using var failingDb = ContextWith(db, new FailWhen(c => c.Id == answered && c.AgentQueuedAt is not null));
        var service = new VerificationService(
            failingDb,
            scope.ServiceProvider.GetRequiredService<VerificationSettings>(),
            factory.Clock,
            scope.ServiceProvider.GetRequiredService<ILogger<VerificationService>>());

        var result = await service.ProcessDueChecksAsync();

        Assert.Equal(new VerificationSweepResultDto(Processed: 2, WorkflowsAwaitingVerification: 0, AskedReporter: 0, QueuedForAgent: 1, Failed: 1), result);

        db.ChangeTracker.Clear();
        var row = await db.VerificationChecks.SingleAsync(v => v.Id == answered);

        // Expired means "nobody answered". Writing it over a yes would erase the answer and
        // pull a real confirmation out of the confirmation rate.
        Assert.Equal(VerificationStatus.Confirmed, row.Status);
        Assert.True(row.ReporterConfirmed);
        Assert.Null(row.ExpiredReason);
        Assert.Null(row.AgentQueuedAt);

        Assert.NotNull((await db.VerificationChecks.SingleAsync(v => v.Id == next)).AgentQueuedAt);
    }

    [Fact]
    public async Task RunSweep_IsFacilitiesManagerOnly_AndReturnsTheCounts()
    {
        using var factory = new SweepClockApiFactory();
        var now = factory.Clock.Now.UtcDateTime;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedAsync(db, "EP1", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(-1));
            await SeedAsync(db, "EP2", VerificationStatus.Pending, askedAt: null, dueAt: now.AddDays(4));
        }

        // No token: who are you?
        var anonymous = await factory.CreateClient().PostAsync("/api/workflows/verification-sweep", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // A valid token with the wrong role: no. Admin included — the policy names one role.
        foreach (var role in new[] { Role.Reporter, Role.Technician, Role.Admin })
        {
            var client = await ClientForAsync(factory, role);
            var refused = await client.PostAsync("/api/workflows/verification-sweep", null);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        var manager = await ClientForAsync(factory, Role.FacilitiesManager);
        var response = await manager.PostAsync("/api/workflows/verification-sweep", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<VerificationSweepResultDto>(JsonOptions);
        Assert.Equal(new VerificationSweepResultDto(Processed: 1, WorkflowsAwaitingVerification: 0, AskedReporter: 1, QueuedForAgent: 0, Failed: 0), result);

        // Pressing it again straight away does nothing new.
        var again = await manager.PostAsync("/api/workflows/verification-sweep", null);
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<VerificationSweepResultDto>(JsonOptions))!.Processed);
    }

    /// <summary>
    /// Step 0 of the sweep — §8's "AwaitingVerification (BackgroundService, N days later)" —
    /// through the real endpoints: a repair completed, the delay stood on to the minute, and
    /// the manual trigger pressed twice.
    ///
    /// IDEMPOTENT: the second pass moves nothing and CREATES nothing — no second check, no
    /// step, no workflow — and leaves the first pass's timestamps exactly as they were.
    /// Verified to fail with the Completed filter removed from the workflow query.
    /// </summary>
    [Fact]
    public async Task Sweep_MovesACompletedWorkflowOnlyOnceItsDelayHasPassed_AndASecondPassChangesNothing()
    {
        using var factory = new SweepClockApiFactory();
        var delayDays = factory.Services.GetRequiredService<VerificationSettings>().DelayDays;
        var scene = await StateMachineScene.BuildAsync(factory);

        var reportId = await scene.FileReportAsync();
        await WorkflowTestData.ReadyForWorkOrderAsync(factory.Services, reportId);

        var raised = await scene.Manager.PostAsJsonAsync("/api/workorders",
            new CreateWorkOrderDto(reportId, scene.AssetId, WorkOrderStrategy.SingleJob, 500m, null), JsonOptions);
        var order = await raised.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, (await scene.Manager.PutAsJsonAsync(
            $"/api/workorders/{order!.Id}/assign", new AssignTechnicianDto(scene.TechnicianId), JsonOptions)).StatusCode);

        // Completed at Start, by the injected clock.
        var completed = await scene.Technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(480m, ServiceOutcome.Resolved, "Replaced the lamp and cleaned the filter.", null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.Equal(WorkflowState.Completed, await WorkflowTestData.StateAsync(factory.Services, reportId));

        // A minute short of the delay: the fault has not had its time to come back.
        factory.Clock.Now = SweepClockApiFactory.Start.AddDays(delayDays).AddMinutes(-1);
        Assert.Equal(new VerificationSweepResultDto(0, 0, 0, 0, 0), await SweepAsync(scene.Manager));
        Assert.Equal(WorkflowState.Completed, await WorkflowTestData.StateAsync(factory.Services, reportId));

        // Exactly the delay: the workflow and its check reach "ask" in the same pass.
        factory.Clock.Now = SweepClockApiFactory.Start.AddDays(delayDays);
        Assert.Equal(
            new VerificationSweepResultDto(Processed: 2, WorkflowsAwaitingVerification: 1, AskedReporter: 1, QueuedForAgent: 0, Failed: 0),
            await SweepAsync(scene.Manager));
        Assert.Equal(WorkflowState.AwaitingVerification, await WorkflowTestData.StateAsync(factory.Services, reportId));

        var afterFirst = await TablesAsync(factory, order.Id);

        // Pressed again: nothing to do, and nothing written.
        Assert.Equal(new VerificationSweepResultDto(0, 0, 0, 0, 0), await SweepAsync(scene.Manager));

        Assert.Equal(afterFirst, await TablesAsync(factory, order.Id));
        Assert.Equal(1, afterFirst.ChecksForOrder);
        Assert.Equal(WorkflowState.AwaitingVerification, await WorkflowTestData.StateAsync(factory.Services, reportId));
    }

    private static async Task<VerificationSweepResultDto> SweepAsync(HttpClient manager)
    {
        var response = await manager.PostAsync("/api/workflows/verification-sweep", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VerificationSweepResultDto>(JsonOptions))!;
    }

    /// <summary>Everything a sweep could create or touch, as one comparable value.</summary>
    private record Tables(
        int Workflows, int Steps, int Checks, int ChecksForOrder, int ServiceRecords,
        DateTime WorkflowUpdatedAt, VerificationStatus CheckStatus, DateTime? CheckProcessedAt);

    private static async Task<Tables> TablesAsync(ApiFactory factory, int orderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var check = await db.VerificationChecks.AsNoTracking().SingleAsync(v => v.WorkOrderId == orderId);

        return new Tables(
            await db.AgentWorkflows.CountAsync(),
            await db.AgentSteps.CountAsync(),
            await db.VerificationChecks.CountAsync(),
            await db.VerificationChecks.CountAsync(v => v.WorkOrderId == orderId),
            await db.ServiceRecords.CountAsync(),
            await db.AgentWorkflows.MaxAsync(w => w.UpdatedAt),
            check.Status,
            check.ProcessedAt);
    }

    // ---------------------------------------------------------------------------

    private static async Task<int> SeedAsync(
        AppDbContext db,
        string prefix,
        VerificationStatus status,
        DateTime? askedAt,
        DateTime? dueAt = null,
        DateTime? respondedAt = null,
        bool? confirmed = null,
        DateTime? queuedAt = null)
    {
        var due = dueAt ?? askedAt!.Value;
        var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, prefix, due.AddDays(-5));

        var check = new VerificationCheck
        {
            WorkOrderId = order.Id,
            AssetId = order.AssetId,
            DueAt = due,
            Status = status,
            ProcessedAt = askedAt,
            ReporterRespondedAt = respondedAt,
            ReporterConfirmed = confirmed,
            AgentQueuedAt = queuedAt
        };

        db.VerificationChecks.Add(check);
        await db.SaveChangesAsync();
        return check.Id;
    }

    /// <summary>Everything a pass could write to a check, per check, read fresh from the database.</summary>
    private sealed record RowState(
        VerificationStatus Status,
        DateTime? ProcessedAt,
        DateTime? AgentQueuedAt,
        string? ExpiredReason,
        DateTime UpdatedAt);

    private static async Task<Dictionary<int, RowState>> SnapshotAsync(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        return await db.VerificationChecks
            .AsNoTracking()
            .ToDictionaryAsync(
                v => v.Id,
                v => new RowState(v.Status, v.ProcessedAt, v.AgentQueuedAt, v.ExpiredReason, v.UpdatedAt));
    }

    /// <summary>
    /// A second context on the same database as <paramref name="db"/>, with an interceptor.
    /// SQLite shares the held-open connection; PostgreSQL reconnects by connection string.
    /// </summary>
    private static AppDbContext ContextWith(AppDbContext db, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();

        if (db.Database.IsNpgsql())
        {
            options.UseNpgsql(db.Database.GetConnectionString());
        }
        else
        {
            options.UseSqlite(db.Database.GetDbConnection());
        }

        return new AppDbContext(options.AddInterceptors(interceptor).Options);
    }

    private static async Task<HttpClient> ClientForAsync(ApiFactory factory, Role role)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SweepPass1", "Test User", role),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }

    /// <summary>
    /// Stands in for a row the database refuses: throws on any save that would write a
    /// modified check matching the predicate, and lets every other save through — including
    /// the one that expires it.
    /// </summary>
    private sealed class FailWhen(Func<VerificationCheck, bool> matches) : SaveChangesInterceptor
    {
        public const string Message = "Simulated bad row.";

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var bad = eventData.Context!.ChangeTracker.Entries<VerificationCheck>()
                .Any(e => e.State == EntityState.Modified && matches(e.Entity));

            return bad
                ? throw new InvalidOperationException(Message)
                : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
