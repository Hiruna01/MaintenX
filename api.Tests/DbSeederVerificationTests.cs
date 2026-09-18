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

        // bool? earns its nullability: three distinct answers across the set.
        Assert.Equal(2, checks.Count(c => c.ReporterConfirmed == true));
        Assert.Equal(1, checks.Count(c => c.ReporterConfirmed == false));
        Assert.Equal(3, checks.Count(c => c.ReporterConfirmed is null));

        // Completion dates spread across a fortnight rather than bunched on one day.
        var completions = checks.Select(c => c.WorkOrder!.CompletedAt!.Value).ToList();
        Assert.True((completions.Max() - completions.Min()).TotalDays > 10);

        // IDEMPOTENT. A second run adds nothing.
        await DbSeeder.SeedAsync(db, configuration, hasher, settings, NullLogger.Instance);
        db.ChangeTracker.Clear();

        Assert.Equal(6, await db.VerificationChecks.CountAsync());
        Assert.Equal(6, await db.WorkOrders.CountAsync());
        Assert.Equal(6, await db.Reports.CountAsync());
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
