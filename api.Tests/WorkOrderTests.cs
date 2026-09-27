using CampusFacilities.Api.Data;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// Model-level tests for the work order tables — the endpoints are in
/// WorkOrderEndpointTests. What is pinned here is the shape of the data itself — the two properties
/// that are cheap to break later and expensive to notice:
///
///   * MONEY SURVIVES A ROUND TRIP EXACTLY. EstimatedCost is compared against the approval
///     threshold, so a value that came back as 14999.989999 would eventually approve
///     spending that was supposed to reach a manager. Changing the column to double would
///     leave everything compiling and this test failing, which is the point.
///   * A RE-SYNCED CLASS CANNOT LAND TWICE. The unique index on ExternalEventId is the
///     whole of that rule, and an index is exactly the kind of enforcement the EF in-memory
///     provider does not have — which is why these run against SQLite or PostgreSQL, never
///     that one.
/// </summary>
public class WorkOrderTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkOrderTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task WorkOrder_RoundTrips_WithExactDecimalCostsAndStampedTimestamps()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (asset, report, _) = await SeedFaultAsync(db, "RTR");

        // Deliberately either side of the default 15,000 threshold, and deliberately with
        // hundredths: these are the values a binary float cannot hold exactly.
        var order = new WorkOrder
        {
            Report = report,
            Asset = asset,
            Strategy = WorkOrderStrategy.KnownFix,
            EstimatedCost = 14_999.99m,
            ActualCost = 15_000.01m
        };

        order.ScheduledSlots.Add(new ScheduledSlot
        {
            StartsAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)
        });

        db.WorkOrders.Add(order);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var loaded = await db.WorkOrders
            .Include(w => w.ScheduledSlots)
            .SingleAsync(w => w.Id == order.Id);

        Assert.Equal(14_999.99m, loaded.EstimatedCost);
        Assert.Equal(15_000.01m, loaded.ActualCost);

        // Exactly on the threshold must compare as "not above it". This is the comparison
        // the approval rule makes, and it is the one a float would eventually get wrong.
        var threshold = scope.ServiceProvider.GetRequiredService<ApprovalSettings>().CostThreshold;
        Assert.Equal(ApprovalSettings.DefaultCostThreshold, threshold);
        Assert.False(15_000.00m > threshold);
        Assert.True(15_000.01m > threshold);

        // Raised as a draft, unassigned, undecided and unfinished — none of which a caller
        // supplies, and all of which later steps set.
        Assert.Equal(WorkOrderStatus.Draft, loaded.Status);
        Assert.Null(loaded.AssignedTechnicianId);
        Assert.Null(loaded.ApprovedByUserId);
        Assert.Null(loaded.ApprovedAt);
        Assert.Null(loaded.CompletedAt);

        // AppDbContext.ApplyTimestamps has to name every timestamped entity; one left off
        // that list compiles, runs, and silently keeps CreatedAt/UpdatedAt at default.
        Assert.NotEqual(default, loaded.CreatedAt);
        Assert.NotEqual(default, loaded.UpdatedAt);
        Assert.NotEqual(default, loaded.ScheduledSlots.Single().CreatedAt);
    }

    [Fact]
    public async Task WorkOrder_Status_AndStrategy_AreStoredAsStringsNotOrdinals()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (asset, report, _) = await SeedFaultAsync(db, "ENM");

        db.WorkOrders.Add(new WorkOrder
        {
            Report = report,
            Asset = asset,
            Status = WorkOrderStatus.AwaitingApproval,
            Strategy = WorkOrderStrategy.EscalateReplacement,
            EstimatedCost = 42_000m
        });
        await db.SaveChangesAsync();

        // Read the raw columns, not the mapped entity: the conversion is what is under test,
        // and reading it back through EF would apply the same conversion in reverse and
        // agree with itself whatever is stored.
        var stored = await db.Database
            .SqlQuery<string>($"""
                SELECT "Status" || '/' || "Strategy" AS "Value"
                FROM "WorkOrders"
                WHERE "Strategy" = 'EscalateReplacement'
                """)
            .ToListAsync();

        Assert.Equal("AwaitingApproval/EscalateReplacement", Assert.Single(stored));
    }

    [Fact]
    public async Task ClassScheduleSlot_RejectsASecondRowForTheSameExternalEvent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, _, room) = await SeedFaultAsync(db, "SYN");

        db.ClassScheduleSlots.Add(NewClass(room, "timetable-evt-1", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        // The same class pulled a second time. Without the unique index this lands as a
        // second lecture in the same room, which reads as a timetable conflict that does
        // not exist and pushes maintenance out of a room that was free.
        db.ClassScheduleSlots.Add(NewClass(room, "timetable-evt-1", new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.ClassScheduleSlots.CountAsync(c => c.ExternalEventId == "timetable-evt-1"));
    }

    // ---------------------------------------------------------------------------------
    // CHECK constraints — the rule at the database, for a writer that skipped every DTO.
    // Written straight through the DbContext, so nothing in C# stands in front of it.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Money is never negative, at the database. Zero is a real estimate and a real cost, and
    /// an ActualCost of NULL is "not recorded yet" — both accepted. Verified to fail with each
    /// constraint removed — on SQLite too, where the column is TEXT.
    /// </summary>
    [Theory]
    [InlineData("-0.01", null, false)]
    [InlineData("100", "-0.01", false)]
    [InlineData("-250000", "-1", false)]
    [InlineData("0", null, true)]
    [InlineData("100", "0", true)]
    [InlineData("0.01", "15000.01", true)]
    public async Task WorkOrderMoney_BelowZero_IsRefusedByTheDatabase(string estimate, string? actual, bool accepted)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (asset, report, _) = await SeedFaultAsync(db, UniquePrefix());

        db.WorkOrders.Add(new WorkOrder
        {
            Report = report,
            Asset = asset,
            Strategy = WorkOrderStrategy.KnownFix,
            // Strings, because an attribute cannot hold a decimal — and never via a double.
            EstimatedCost = decimal.Parse(estimate, System.Globalization.CultureInfo.InvariantCulture),
            ActualCost = actual is null ? null : decimal.Parse(actual, System.Globalization.CultureInfo.InvariantCulture)
        });

        if (accepted)
        {
            await db.SaveChangesAsync();
            return;
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        Assert.False(await db.WorkOrders.AnyAsync(w => w.ReportId == report.Id));
    }

    /// <summary>
    /// A booked visit and a mirrored class both end after they start — a backwards or
    /// zero-length block is refused, one minute is not. Verified to fail with each constraint
    /// removed.
    /// </summary>
    [Theory]
    [InlineData(-60, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task SlotsThatDoNotEndAfterTheyStart_AreRefusedByTheDatabase(int minutes, bool accepted)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (asset, report, room) = await SeedFaultAsync(db, UniquePrefix());

        var order = new WorkOrder
        {
            Report = report,
            Asset = asset,
            Strategy = WorkOrderStrategy.KnownFix,
            EstimatedCost = 500m
        };
        db.WorkOrders.Add(order);
        await db.SaveChangesAsync();

        var startsAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        db.ScheduledSlots.Add(new ScheduledSlot
        {
            WorkOrderId = order.Id,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(minutes)
        });
        await AssertSavedOrRefusedAsync(db, accepted);

        // By id, not navigation: a refused save cleared the tracker, and a detached Room
        // would be inserted a second time.
        db.ClassScheduleSlots.Add(new ClassScheduleSlot
        {
            RoomId = room.Id,
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(minutes),
            Title = "SE3090 Lecture",
            ExternalEventId = $"evt-{Guid.NewGuid():N}",
            SyncedAt = DateTime.UtcNow
        });
        await AssertSavedOrRefusedAsync(db, accepted);

        Assert.Equal(accepted ? 1 : 0, await db.ScheduledSlots.CountAsync(s => s.WorkOrderId == order.Id));
        Assert.Equal(accepted ? 1 : 0, await db.ClassScheduleSlots.CountAsync(c => c.RoomId == room.Id));
    }

    private static async Task AssertSavedOrRefusedAsync(AppDbContext db, bool accepted)
    {
        if (accepted)
        {
            await db.SaveChangesAsync();
            return;
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }

    private static string UniquePrefix() => "C" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

    private static ClassScheduleSlot NewClass(Room room, string externalEventId, DateTime startsAt) => new()
    {
        Room = room,
        StartsAt = startsAt,
        EndsAt = startsAt.AddHours(2),
        Title = "SE3090 Lecture",
        ExternalEventId = externalEventId,
        SyncedAt = DateTime.UtcNow
    };

    /// <summary>
    /// The minimum a work order needs to exist: a room, a reporter, an asset in that room
    /// and a report against it. The prefix keeps the unique codes apart — xUnit gives this
    /// class one factory, so every test in it shares one database.
    /// </summary>
    private static async Task<(Asset Asset, Report Report, Room Room)> SeedFaultAsync(
        AppDbContext db, string prefix)
    {
        var building = new Building { Name = $"Block {prefix}", Code = prefix };
        var room = new Room { Building = building, Name = "Lecture Hall", Code = $"{prefix}101", Floor = 1 };
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
            Description = "Projector keeps cutting out mid-lecture."
        };

        db.AddRange(building, room, reporter, category, asset, report);
        await db.SaveChangesAsync();

        return (asset, report, room);
    }
}
