using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace api.Tests;

/// <summary>
/// ApiFactory with the clock pinned to <see cref="Today"/>.
///
/// The failure summary is date arithmetic against "today", and the cases worth testing sit
/// exactly on a boundary — a warranty expiring today, a visit exactly 90 days ago. Against
/// the wall clock those would flip whenever a run straddled midnight UTC, which is the
/// worst kind of flaky test: the one that fails on the boundary it exists to check.
///
/// 31 May is chosen on purpose. Three calendar months before it is 28 February — 92 days —
/// while 90 days before it is 2 March. So a visit on 1 March is inside
/// <c>AddMonths(-3)</c> but outside the 90-day window, and the test that uses it can tell
/// the rule that was written from the one that was deliberately not.
/// </summary>
public class FixedClockApiFactory : ApiFactory
{
    public static readonly DateOnly Today = new(2026, 5, 31);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            // Midday UTC, so no conversion anywhere can move it onto another date.
            services.AddSingleton<TimeProvider>(
                new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// GET /api/assets/{id}/failure-summary, on its boundaries.
///
/// The summary is the registry's business operation and every field is a deterministic
/// rule in C#, so the claim being tested is that the same history always gives the same
/// answer — and the place a rule like that goes wrong is the edge, not the middle. Each
/// test therefore sits on one: exactly two visits and exactly three, day 90 and day 91,
/// a warranty expiring yesterday, today and tomorrow.
///
/// The broad "known history in, known numbers out" case and the never-serviced case live in
/// AssetTests; this class is only the edges.
/// </summary>
public class FailureSummaryTests : IClassFixture<FixedClockApiFactory>
{
    private static readonly DateOnly Today = FixedClockApiFactory.Today;

    private readonly FixedClockApiFactory _factory;

    public FailureSummaryTests(FixedClockApiFactory factory) => _factory = factory;

    // The API serialises enums by name, so the tests must too.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // -----------------------------------------------------------------------
    // isRepeatFailure — three or more visits inside the 90-day window
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExactlyThreeVisitsInsideNinetyDays_IsARepeatFailure()
    {
        var (admin, assetId) = await CreateAssetAsync();

        await SeedHistoryAsync(assetId, Today.AddDays(-10), Today.AddDays(-40), Today.AddDays(-80));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(3, summary.FailureCount3Months);
        Assert.True(summary.IsRepeatFailure);
    }

    [Fact]
    public async Task ExactlyTwoVisitsInsideNinetyDays_IsNotARepeatFailure_HoweverManyAreOlder()
    {
        var (admin, assetId) = await CreateAssetAsync();

        // Two inside the window and two outside it. The old ones still count towards the
        // year, which is what makes this a test of the window rather than of the total.
        await SeedHistoryAsync(assetId,
            Today.AddDays(-10), Today.AddDays(-80),
            Today.AddDays(-120), Today.AddDays(-200));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(2, summary.FailureCount3Months);
        Assert.Equal(4, summary.FailureCount12Months);
        Assert.False(summary.IsRepeatFailure);
    }

    /// <summary>
    /// The edge of the window itself. With Today fixed at 31 May: 90 days back is 2 March,
    /// which is inside; 91 days back is 1 March, which is outside — even though
    /// <c>AddMonths(-3)</c> (28 February) would have counted it. "Three months" here means
    /// exactly 90 days, and the flag reads the same cut-off as the count.
    /// </summary>
    [Theory]
    [InlineData(90, 3, true)]
    [InlineData(91, 2, false)]
    public async Task TheWindowIsExactlyNinetyDays_NotThreeCalendarMonths(
        int oldestVisitDaysAgo, int expectedCount, bool expectedRepeat)
    {
        var (admin, assetId) = await CreateAssetAsync();

        await SeedHistoryAsync(assetId,
            Today.AddDays(-5), Today.AddDays(-30), Today.AddDays(-oldestVisitDaysAgo));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(expectedCount, summary.FailureCount3Months);
        Assert.Equal(expectedRepeat, summary.IsRepeatFailure);

        // The flag and the count can never disagree on the same screen.
        Assert.Equal(summary.FailureCount3Months >= 3, summary.IsRepeatFailure);
    }

    [Fact]
    public async Task NinetyOneDaysAgo_IsOneMarch_WhichThreeCalendarMonthsWouldHaveIncluded()
    {
        // The same boundary as the theory above, written as dates so the reason reads
        // without arithmetic: AddMonths(-3) from 31 May is 28 February.
        Assert.Equal(new DateOnly(2026, 3, 1), Today.AddDays(-91));
        Assert.Equal(new DateOnly(2026, 2, 28), Today.AddMonths(-3));

        var (admin, assetId) = await CreateAssetAsync();
        await SeedHistoryAsync(assetId, new DateOnly(2026, 3, 1));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(0, summary.FailureCount3Months);
        Assert.Equal(1, summary.FailureCount12Months);
    }

    /// <summary>The twelve-month window is inclusive of the same day one year ago.</summary>
    [Theory]
    [InlineData(2025, 5, 31, 1)]
    [InlineData(2025, 5, 30, 0)]
    public async Task TheYearWindow_IncludesTheSameDayLastYear_AndNotTheDayBefore(
        int year, int month, int day, int expectedCount)
    {
        var (admin, assetId) = await CreateAssetAsync();
        await SeedHistoryAsync(assetId, new DateOnly(year, month, day));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(expectedCount, summary.FailureCount12Months);
    }

    [Fact]
    public async Task AVisitToday_IsZeroDaysAgo_NotNull()
    {
        var (admin, assetId) = await CreateAssetAsync();
        await SeedHistoryAsync(assetId, Today);

        var summary = await GetSummaryAsync(admin, assetId);

        // Zero is "serviced today"; null is "never serviced". The same field must not be
        // able to say both.
        Assert.Equal(Today, summary.LastServicedOn);
        Assert.Equal(0, summary.DaysSinceLastService);
        Assert.Equal(1, summary.FailureCount3Months);
    }

    // -----------------------------------------------------------------------
    // isUnderWarranty — covered up to and including the expiry date
    // -----------------------------------------------------------------------

    /// <summary>
    /// A warranty runs to the end of the day it expires on — that is why the column is a
    /// DateOnly — so expiring TODAY is still covered, and yesterday is not. A date in the
    /// past of any distance reads false.
    /// </summary>
    [Theory]
    [InlineData(-365, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Warranty_IsCoveredThroughItsExpiryDate_AndNotTheDayAfter(
        int expiresInDays, bool expectedCovered)
    {
        var (admin, assetId) = await CreateAssetAsync(warrantyExpiresOn: Today.AddDays(expiresInDays));

        var summary = await GetSummaryAsync(admin, assetId);

        Assert.Equal(expectedCovered, summary.IsUnderWarranty);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<AssetFailureSummaryDto> GetSummaryAsync(HttpClient client, int assetId)
    {
        var response = await client.GetAsync($"/api/assets/{assetId}/failure-summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<AssetFailureSummaryDto>(JsonOptions);
        return summary!;
    }

    /// <summary>A fresh Admin, category, room and asset — every test gets its own history.</summary>
    private async Task<(HttpClient Admin, int AssetId)> CreateAssetAsync(DateOnly? warrantyExpiresOn = null)
    {
        var admin = _factory.CreateClient();

        var registered = await admin.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SummaryPass1", "Test Admin", Role.Admin),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var auth = await registered.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var building = await (await admin.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Main Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await admin.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Category {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var created = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto($"SUM-{UniqueCode()}", "Projector", category!.Id, room!.Id, null, null,
                new DateOnly(2023, 1, 10), warrantyExpiresOn),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var asset = await created.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);
        return (admin, asset!.Id);
    }

    /// <summary>
    /// Writes service history straight to the database: a ServiceRecord is appended when a
    /// work order completes and is never posted by a client, so there is no endpoint to use.
    /// The outcome is irrelevant to every count tested here — a visit is a visit.
    /// </summary>
    private async Task SeedHistoryAsync(int assetId, params DateOnly[] visits)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var visit in visits)
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                AssetId = assetId,
                ServicedOn = visit,
                TechnicianName = "K. Perera",
                TechnicianNote = "chk unit, no fault found on test",
                Outcome = ServiceOutcome.NoFaultFound
            });
        }

        await db.SaveChangesAsync();
    }
}
