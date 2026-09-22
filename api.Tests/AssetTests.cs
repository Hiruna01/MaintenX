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
/// The asset registry endpoints. Three things here are worth more than the CRUD coverage:
///
///   * DELETE DOES NOT DELETE. It retires. The row and its service history have to still
///     be there afterwards, because that history is what the diagnostic agent reads and it
///     outlives the machine it describes.
///   * THE FAILURE SUMMARY IS ARITHMETIC. Given a known history it must produce known
///     numbers — that is the whole claim being made by keeping it out of a prompt.
///   * 401 AND 403 STAY DISTINCT. No token is "who are you", a Reporter's token on a write
///     is "I know who you are, and no". An evaluator may ask for both.
/// </summary>
public class AssetTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AssetTests(ApiFactory factory) => _factory = factory;

    // The API serialises enums by name, so the tests must too — otherwise they would pass
    // against a contract the real clients cannot use.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<HttpClient> CreateAuthenticatedClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "AssetPass1", "Test User", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }

    private async Task<int> CreateRoomAsync()
    {
        var client = _factory.CreateClient();

        var buildingResponse = await client.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await client.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomDto(building!.Id, "Lab", UniqueCode(), 1), JsonOptions);

        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);
        return room!.Id;
    }

    private async Task<int> CreateCategoryAsync(HttpClient adminClient, int warrantyMonths = 24)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/assetcategories",
            new CreateAssetCategoryDto($"Category {UniqueCode()}", warrantyMonths), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var category = await response.Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);
        return category!.Id;
    }

    private async Task<AssetDto> CreateAssetAsync(
        HttpClient adminClient,
        string? name = null,
        string? tag = null,
        int? categoryId = null,
        int? roomId = null,
        DateOnly? installedOn = null,
        DateOnly? warrantyExpiresOn = null)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(
                tag ?? $"AST-{UniqueCode()}",
                name ?? "Projector",
                categoryId ?? await CreateCategoryAsync(adminClient),
                roomId ?? await CreateRoomAsync(),
                "Epson",
                "EB-X05",
                installedOn ?? new DateOnly(2023, 1, 10),
                warrantyExpiresOn),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var asset = await response.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);
        return asset!;
    }

    // -----------------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAssets_Searches_FiltersAndPages_ThroughPagedResult()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var roomId = await CreateRoomAsync();
        var categoryId = await CreateCategoryAsync(admin);
        var marker = UniqueCode();

        // Three in the same room and category, one of them elsewhere, so the filters have
        // something to exclude rather than trivially matching everything.
        await CreateAssetAsync(admin, $"Alpha {marker}", $"AAA-{marker}", categoryId, roomId,
            new DateOnly(2020, 5, 1));
        await CreateAssetAsync(admin, $"Bravo {marker}", $"BBB-{marker}", categoryId, roomId,
            new DateOnly(2019, 5, 1));
        await CreateAssetAsync(admin, $"Charlie {marker}", $"CCC-{marker}", categoryId, roomId,
            new DateOnly(2021, 5, 1));
        await CreateAssetAsync(admin, $"Elsewhere {marker}", $"ZZZ-{marker}");

        // Search by name, lower-cased by the caller: the match must be case-insensitive on
        // SQLite and PostgreSQL alike.
        var byName = await admin.GetFromJsonAsync<PagedResult<AssetDto>>(
            $"/api/assets?search={marker.ToLowerInvariant()}", JsonOptions);
        Assert.Equal(4, byName!.TotalCount);

        // Search by tag — the same box, because a scanned or typed tag is what a user has.
        var byTag = await admin.GetFromJsonAsync<PagedResult<AssetDto>>(
            $"/api/assets?search=AAA-{marker}", JsonOptions);
        Assert.Equal($"AAA-{marker}", Assert.Single(byTag!.Items).AssetTag);

        // Filters combine with the search.
        var filtered = await admin.GetFromJsonAsync<PagedResult<AssetDto>>(
            $"/api/assets?search={marker}&roomId={roomId}&categoryId={categoryId}&status=Active",
            JsonOptions);
        Assert.Equal(3, filtered!.TotalCount);

        // Sorted by name, and paged: page 1 of 2 carries the counters a client renders.
        var firstPage = await admin.GetFromJsonAsync<PagedResult<AssetDto>>(
            $"/api/assets?search={marker}&roomId={roomId}&sort=Name&page=1&pageSize=2", JsonOptions);
        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(new[] { $"Alpha {marker}", $"Bravo {marker}" },
            firstPage.Items.Select(a => a.Name));

        // Sorted by installation date instead — oldest first, a different order from above.
        var byAge = await admin.GetFromJsonAsync<PagedResult<AssetDto>>(
            $"/api/assets?search={marker}&roomId={roomId}&sort=InstalledOn", JsonOptions);
        Assert.Equal(
            new[] { $"Bravo {marker}", $"Alpha {marker}", $"Charlie {marker}" },
            byAge!.Items.Select(a => a.Name));
    }

    [Fact]
    public async Task GetAsset_ById_ReturnsDetailWithHistoryOldestFirst_And404ForUnknown()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);

        await SeedHistoryAsync(asset.Id,
            (new DateOnly(2025, 3, 1), ServiceOutcome.TemporaryFix),
            (new DateOnly(2024, 1, 15), ServiceOutcome.NoFaultFound),
            (new DateOnly(2025, 7, 20), ServiceOutcome.PartReplaced));

        var detail = await admin.GetFromJsonAsync<AssetDetailDto>($"/api/assets/{asset.Id}", JsonOptions);

        Assert.Equal(asset.AssetTag, detail!.AssetTag);
        Assert.NotNull(detail.Category);
        Assert.NotNull(detail.Room);

        // Oldest first — a repeat failure only reads as one in the order it happened.
        Assert.Equal(
            new[] { new DateOnly(2024, 1, 15), new DateOnly(2025, 3, 1), new DateOnly(2025, 7, 20) },
            detail.ServiceHistory.Select(s => s.ServicedOn));

        var missing = await admin.GetAsync("/api/assets/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetAssetByTag_IsTheQrPath_AndAnUnknownStickerIs404()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);

        var detail = await admin.GetFromJsonAsync<AssetDetailDto>(
            $"/api/assets/by-tag/{asset.AssetTag}", JsonOptions);

        Assert.Equal(asset.Id, detail!.Id);

        // A sticker from some other system is a miss, not an error.
        var unknown = await admin.GetAsync($"/api/assets/by-tag/NOT-A-REAL-TAG-{UniqueCode()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // -----------------------------------------------------------------------
    // The business operation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FailureSummary_IsCountsAndDateComparisons_OverAKnownHistory()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // A warranty that has not run out yet, and a history shaped like the planted
        // repeat-failure pattern: three visits inside ninety days, two of them temporary
        // fixes, plus one old visit that is inside twelve months but outside the quarter.
        var asset = await CreateAssetAsync(admin, warrantyExpiresOn: today.AddDays(30));

        await SeedHistoryAsync(asset.Id,
            (today.AddDays(-200), ServiceOutcome.NoFaultFound),
            (today.AddDays(-80), ServiceOutcome.TemporaryFix),
            (today.AddDays(-40), ServiceOutcome.TemporaryFix),
            (today.AddDays(-7), ServiceOutcome.Resolved));

        var summary = await admin.GetFromJsonAsync<AssetFailureSummaryDto>(
            $"/api/assets/{asset.Id}/failure-summary", JsonOptions);

        Assert.Equal(asset.Id, summary!.AssetId);
        Assert.Equal(asset.AssetTag, summary.AssetTag);
        Assert.Equal(4, summary.FailureCount12Months);
        Assert.Equal(3, summary.FailureCount3Months);
        Assert.Equal(today.AddDays(-7), summary.LastServicedOn);
        Assert.Equal(7, summary.DaysSinceLastService);
        Assert.Equal(2, summary.TemporaryFixCount);
        Assert.True(summary.IsUnderWarranty);

        // Three or more inside ninety days. The flag and the count read the same window,
        // so they can never contradict each other on the same screen.
        Assert.True(summary.IsRepeatFailure);
        Assert.Equal(summary.FailureCount3Months >= 3, summary.IsRepeatFailure);

        Assert.Equal(
            new[] { ServiceOutcome.Resolved, ServiceOutcome.TemporaryFix, ServiceOutcome.NoFaultFound },
            summary.DistinctOutcomes);
    }

    [Fact]
    public async Task FailureSummary_OnAnUntouchedAsset_SaysNeverServicedRatherThanZero()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);

        // An expired warranty and a null one are different facts; both read as "not covered".
        var expired = await CreateAssetAsync(
            admin, warrantyExpiresOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        var noWarranty = await CreateAssetAsync(admin);

        var summary = await admin.GetFromJsonAsync<AssetFailureSummaryDto>(
            $"/api/assets/{expired.Id}/failure-summary", JsonOptions);

        Assert.Equal(0, summary!.FailureCount12Months);
        Assert.Equal(0, summary.FailureCount3Months);
        Assert.False(summary.IsRepeatFailure);
        Assert.False(summary.IsUnderWarranty);
        Assert.Empty(summary.DistinctOutcomes);

        // Null, not zero: a machine nobody has touched is not one serviced today.
        Assert.Null(summary.LastServicedOn);
        Assert.Null(summary.DaysSinceLastService);

        var unrecorded = await admin.GetFromJsonAsync<AssetFailureSummaryDto>(
            $"/api/assets/{noWarranty.Id}/failure-summary", JsonOptions);
        Assert.False(unrecorded!.IsUnderWarranty);

        var missing = await admin.GetAsync("/api/assets/999999/failure-summary");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsset_Returns201WithLocation_409ForADuplicateTag_And400ForABadReference()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var roomId = await CreateRoomAsync();
        var categoryId = await CreateCategoryAsync(admin);
        var tag = $"DUP-{UniqueCode()}";

        var created = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(tag, "Projector", categoryId, roomId, null, null,
                new DateOnly(2024, 2, 2), null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        // The Location header has to actually resolve — that is the point of CreatedAtAction.
        var followed = await admin.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);

        // Every asset enters the estate in service; CreateAssetDto carries no status.
        var asset = await created.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);
        Assert.Equal(AssetStatus.Active, asset!.Status);

        // The tag is the QR payload and it identifies exactly one machine.
        var duplicate = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(tag, "Another projector", categoryId, roomId, null, null,
                new DateOnly(2024, 2, 2), null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // A bad foreign key is the caller's mistake, so a 400 — not a constraint violation
        // surfacing as a 500.
        var badCategory = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto($"NEW-{UniqueCode()}", "Projector", 999999, roomId, null, null,
                new DateOnly(2024, 2, 2), null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
    }

    [Fact]
    public async Task UpdateAsset_Returns204_404ForUnknown_AndCannotChangeTheTag()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var newRoomId = await CreateRoomAsync();
        var newCategoryId = await CreateCategoryAsync(admin);

        var response = await admin.PutAsJsonAsync(
            $"/api/assets/{asset.Id}",
            new UpdateAssetDto("Renamed projector", newCategoryId, newRoomId, "Epson", "EB-X06",
                new DateOnly(2023, 1, 10), null, AssetStatus.UnderMaintenance),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await admin.GetFromJsonAsync<AssetDetailDto>($"/api/assets/{asset.Id}", JsonOptions);
        Assert.Equal("Renamed projector", updated!.Name);
        Assert.Equal(AssetStatus.UnderMaintenance, updated.Status);

        // UpdateAssetDto has no AssetTag field at all, so the sticker on the wall still
        // finds this row. This assertion is what a re-added field would break.
        Assert.Equal(asset.AssetTag, updated.AssetTag);

        var missing = await admin.PutAsJsonAsync(
            "/api/assets/999999",
            new UpdateAssetDto("Ghost", newCategoryId, newRoomId, null, null,
                new DateOnly(2023, 1, 10), null, AssetStatus.Active),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeleteAsset_RetiresIt_AndKeepsTheRowAndItsServiceHistory()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);

        await SeedHistoryAsync(asset.Id, (new DateOnly(2024, 6, 1), ServiceOutcome.PartReplaced));

        var response = await admin.DeleteAsync($"/api/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // THE ROW IS STILL THERE. Assets are never deleted: this one carries a service
        // history the diagnostic agent reads, and that history outlives the machine.
        var detail = await admin.GetFromJsonAsync<AssetDetailDto>($"/api/assets/{asset.Id}", JsonOptions);
        Assert.Equal(AssetStatus.Retired, detail!.Status);
        Assert.Single(detail.ServiceHistory);

        // Asking again for something already out of service is not a failure.
        var again = await admin.DeleteAsync($"/api/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        var missing = await admin.DeleteAsync("/api/assets/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // -----------------------------------------------------------------------
    // 401 vs 403
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reads_NeedATokenOnly_ButWritesAreAdminOnly()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);

        // No token at all: who are you?
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.DeleteAsync($"/api/assets/{asset.Id}")).StatusCode);

        // A valid token, the wrong role: I know who you are, and no. A reporter still
        // reads the registry — that is the point of the QR lookup.
        var reporter = await CreateAuthenticatedClientAsync(Role.Reporter);
        Assert.Equal(HttpStatusCode.OK, (await reporter.GetAsync("/api/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await reporter.GetAsync($"/api/assets/by-tag/{asset.AssetTag}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await reporter.GetAsync($"/api/assets/{asset.Id}/failure-summary")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await reporter.DeleteAsync($"/api/assets/{asset.Id}")).StatusCode);

        var forbiddenCreate = await reporter.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto($"NEW-{UniqueCode()}", "Projector", 1, 1, null, null,
                new DateOnly(2024, 1, 1), null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);

        var forbiddenCategory = await reporter.PostAsJsonAsync(
            "/api/assetcategories",
            new CreateAssetCategoryDto($"Category {UniqueCode()}", 12), JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCategory.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Categories_Create_Read_Update_AndRejectADuplicateName()
    {
        var admin = await CreateAuthenticatedClientAsync(Role.Admin);
        var name = $"Category {UniqueCode()}";

        var created = await admin.PostAsJsonAsync(
            "/api/assetcategories", new CreateAssetCategoryDto(name, 24), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var category = await created.Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);
        Assert.Equal(name, category!.Name);
        Assert.Equal(24, category.DefaultWarrantyMonths);

        var fetched = await admin.GetFromJsonAsync<AssetCategoryDto>(
            $"/api/assetcategories/{category.Id}", JsonOptions);
        Assert.Equal(category.Id, fetched!.Id);

        var all = await admin.GetFromJsonAsync<List<AssetCategoryDto>>("/api/assetcategories", JsonOptions);
        Assert.Contains(all!, c => c.Id == category.Id);

        // Two categories with the same name make a picker nobody can choose from.
        var duplicate = await admin.PostAsJsonAsync(
            "/api/assetcategories", new CreateAssetCategoryDto(name, 12), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var renamed = $"{name} renamed";
        var updated = await admin.PutAsJsonAsync(
            $"/api/assetcategories/{category.Id}",
            new CreateAssetCategoryDto(renamed, 36), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var after = await admin.GetFromJsonAsync<AssetCategoryDto>(
            $"/api/assetcategories/{category.Id}", JsonOptions);
        Assert.Equal(renamed, after!.Name);
        Assert.Equal(36, after.DefaultWarrantyMonths);

        // Renaming a category to the name it already has is an update, not a conflict.
        var noop = await admin.PutAsJsonAsync(
            $"/api/assetcategories/{category.Id}",
            new CreateAssetCategoryDto(renamed, 36), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, noop.StatusCode);

        // But taking another category's name is.
        var other = await CreateCategoryAsync(admin);
        var collision = await admin.PutAsJsonAsync(
            $"/api/assetcategories/{other}",
            new CreateAssetCategoryDto(renamed, 12), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);

        var missing = await admin.PutAsJsonAsync(
            "/api/assetcategories/999999",
            new CreateAssetCategoryDto($"Category {UniqueCode()}", 12), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/assetcategories/999999")).StatusCode);
    }

    /// <summary>
    /// Writes service history straight to the database. There is no endpoint for it — a
    /// ServiceRecord is appended when a work order completes, never posted by a client.
    /// </summary>
    private async Task SeedHistoryAsync(int assetId, params (DateOnly On, ServiceOutcome Outcome)[] visits)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var visit in visits)
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                AssetId = assetId,
                ServicedOn = visit.On,
                TechnicianName = "S. Perera",
                TechnicianNote = "chk unit, temp fix applied",
                Outcome = visit.Outcome
            });
        }

        await db.SaveChangesAsync();
    }
}
