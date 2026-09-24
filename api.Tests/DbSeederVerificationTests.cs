using CampusFacilities.Api.Data;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace api.Tests;

/// <summary>
/// The Development seeder's verification data, which the sweep and the metrics endpoint
/// are developed against.
///
/// The seeder never runs during tests — ApiFactory uses UseEnvironment("Testing")
/// precisely so it cannot — so it is invoked directly here. Worth pinning because its two
/// properties are both invisible when broken: a seed that quietly stops producing overdue
/// checks leaves the sweep with nothing to pick up and looks like a bug in the sweep, and
/// a seed that stops being idempotent duplicates everything a bit more on every restart.
/// </summary>
public class DbSeederVerificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DbSeederVerificationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Seeder_ProducesTheDocumentedVerificationShape_AndIsIdempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var settings = new VerificationSettings();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Passwords:Reporter"] = "seed-test-password",
                ["Seed:Passwords:Technician"] = "seed-test-password",
                ["Seed:Passwords:FacilitiesManager"] = "seed-test-password"
            })
            .Build();

        await DbSeeder.SeedAsync(db, configuration, hasher, settings, NullLogger.Instance);

        db.ChangeTracker.Clear();

        var checks = await db.VerificationChecks
            .Include(v => v.Asset)
            .Include(v => v.WorkOrder)
            .ToListAsync();

        // Six completed work orders, each with exactly one check.
        Assert.Equal(6, checks.Count);
        Assert.Equal(6, await db.WorkOrders.CountAsync(w => w.Status == WorkOrderStatus.Completed));
        Assert.Equal(6, checks.Select(c => c.WorkOrderId).Distinct().Count());

        // 2 Confirmed / 1 Reopened / 3 Pending, as specified.
        Assert.Equal(2, checks.Count(c => c.Status == VerificationStatus.Confirmed));
        Assert.Equal(1, checks.Count(c => c.Status == VerificationStatus.Reopened));
        Assert.Equal(3, checks.Count(c => c.Status == VerificationStatus.Pending));

        // All three Pending checks are ALREADY OVERDUE, so the sweep has work the first
        // time it runs. This is the property most likely to rot: it depends on the seeded
        // completion dates staying further back than VerificationSettings.DelayDays.
        var now = DateTime.UtcNow;
        Assert.All(
            checks.Where(c => c.Status == VerificationStatus.Pending),
            c => Assert.True(c.DueAt < now, $"Pending check {c.Id} is due at {c.DueAt}, not yet overdue."));

        // The reopened one sits on the projector with the planted repeat-failure history,
        // so an escalation rule has a real case to find.
        var reopened = Assert.Single(checks.Where(c => c.Status == VerificationStatus.Reopened));
        Assert.Equal("PRJ-MAB101-01", reopened.Asset!.AssetTag);
        Assert.False(reopened.ReporterConfirmed);
        Assert.NotNull(reopened.AgentOutcome);
        // Evidence travels with the verdict: the detail page renders one line per item.
        Assert.NotEmpty(System.Text.Json.JsonSerializer.Deserialize<string[]>(reopened.AgentEvidenceJson!)!);

        // bool? earns its nullability: three distinct answers across the set.
        Assert.Equal(2, checks.Count(c => c.ReporterConfirmed == true));
        Assert.Equal(1, checks.Count(c => c.ReporterConfirmed == false));
        Assert.Equal(3, checks.Count(c => c.ReporterConfirmed is null));

        // Completion dates spread across a fortnight rather than bunched on one day.
        var completions = checks.Select(c => c.WorkOrder!.CompletedAt!.Value).ToList();
        Assert.True((completions.Max() - completions.Min()).TotalDays > 10);

        // IDEMPOTENT. A second run adds nothing: 6 completed + 3 live orders, a report each.
        await DbSeeder.SeedAsync(db, configuration, hasher, settings, NullLogger.Instance);
        db.ChangeTracker.Clear();

        Assert.Equal(6, await db.VerificationChecks.CountAsync());
        Assert.Equal(9, await db.WorkOrders.CountAsync());
        Assert.Equal(9, await db.Reports.CountAsync());
        Assert.Equal(2, await db.AgentWorkflows.CountAsync());
        Assert.Equal(6, await db.AgentSteps.CountAsync());
    }

    [Fact]
    public async Task Seeder_FillsTheApprovalQueue_WithADiagnosisAndProposalTheQueueCanRead()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Passwords:Reporter"] = "seed-test-password"
            })
            .Build();

        await DbSeeder.SeedAsync(db, configuration, hasher, new VerificationSettings(), NullLogger.Instance);
        db.ChangeTracker.Clear();

        // Read through the real service, so the seeded steps are proven to be in the shape
        // the queue reads — the same code path as a real run's.
        var queue = await scope.ServiceProvider.GetRequiredService<IWorkOrderService>().GetApprovalQueueAsync();

        Assert.Equal(2, queue.TotalCount);

        // The golden projector: above the threshold AND a replacement, so both halves of the
        // gate show, with the thermal diagnosis the live eval produced — and no compressor.
        var projector = Assert.Single(queue.Items, c => c.Asset.AssetTag == "PRJ-MAB101-01");
        Assert.Equal(WorkOrderStrategy.EscalateReplacement, projector.WorkOrder.Strategy);
        Assert.True(projector.WorkOrder.ApprovalBasis.ExceedsThreshold);
        Assert.True(projector.WorkOrder.ApprovalBasis.IsReplacement);
        Assert.True(projector.Diagnosis!.OutputReadable);
        Assert.Contains("cooling fan", projector.Diagnosis.Hypotheses[0].Cause);
        Assert.DoesNotContain(projector.Diagnosis.Hypotheses,
            h => h.Cause.Contains("compressor", StringComparison.OrdinalIgnoreCase));
        Assert.True(projector.Proposal!.OutputReadable);
        Assert.Equal(45_000m, projector.Proposal.EstimatedCost);
        Assert.Equal(3, projector.Asset.ServiceHistory.Count);

        // The air conditioner: above the threshold on cost alone.
        var aircon = Assert.Single(queue.Items, c => c.Asset.AssetTag == "ACU-ENG101-01");
        Assert.True(aircon.WorkOrder.ApprovalBasis.ExceedsThreshold);
        Assert.False(aircon.WorkOrder.ApprovalBasis.IsReplacement);
        Assert.True(aircon.Proposal!.OutputReadable);

        // And one approved order nobody is assigned to yet, for the dispatch board.
        var unassigned = await db.WorkOrders.SingleAsync(
            w => w.Status == WorkOrderStatus.Approved && w.AssignedTechnicianId == null);
        Assert.Null(unassigned.ApprovedByUserId);
    }

    [Fact]
    public async Task Seeder_SkipsWorkOrders_WhenNoDemoReporterCouldBeCreated()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        // No Seed:Passwords:* at all, so no demo users are created — the configuration a
        // teammate who has not set up user secrets actually has.
        var configuration = new ConfigurationBuilder().Build();

        await DbSeeder.SeedAsync(db, configuration, hasher, new VerificationSettings(), NullLogger.Instance);

        // Assets still seed; work orders back out rather than throwing on the missing
        // reporter, so a half-configured machine still gets a usable registry.
        Assert.True(await db.Assets.AnyAsync());
        Assert.Equal(0, await db.VerificationChecks.CountAsync());
        Assert.Equal(0, await db.WorkOrders.CountAsync());
    }
}
