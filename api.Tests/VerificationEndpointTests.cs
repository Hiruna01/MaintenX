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
/// The reporter-facing verification endpoints and the metrics read, through the real
/// pipeline. What is pinned:
///
///   * the confirm checks in their order — 404, 403 for anyone but the reporter (a manager
///     included), 409 before the check falls due, 409 once answered — and a success that
///     writes the answer, its status and the agent hand-off together;
///   * the answer is bounded: a missing yes/no is a 400, not a "no", and the comment stops
///     at 300 characters;
///   * a Reporter's list and detail reads are scoped to their own reports, in the service;
///   * the metrics are FacilitiesManager only, 401 and 403 kept apart.
///
/// The sweep and the answer's effect on the metrics are VerificationSweepTests' and
/// VerificationTests' — not repeated here.
/// </summary>
public class VerificationEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public VerificationEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_RunsItsChecksInOrder_AndRecordsTheAnswerWithTheAgentHandOff()
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);
        var (stranger, _) = await ClientForAsync(Role.Reporter);
        var (manager, _) = await ClientForAsync(Role.FacilitiesManager);

        var check = await SeedCheckAsync("CNF", reporterId, VerificationStatus.AwaitingReporterResponse);
        var body = new ReporterConfirmationDto(Confirmed: false, Comment: "  Still cuts out after an hour.  ");

        // No token: who are you?
        var anonymous = await _factory.CreateClient().PostAsJsonAsync(ConfirmUrl(check), body, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await reporter.PostAsJsonAsync(ConfirmUrl(check + 10_000), body, JsonOptions)).StatusCode);

        // Somebody else's check — another reporter, and a manager too. Nobody answers on the
        // reporter's behalf.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync(ConfirmUrl(check), body, JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync(ConfirmUrl(check), body, JsonOptions)).StatusCode);

        var answered = await reporter.PostAsJsonAsync(ConfirmUrl(check), body, JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.VerificationChecks.AsNoTracking().SingleAsync(v => v.Id == check);

            Assert.Equal(VerificationStatus.Reopened, row.Status);
            Assert.False(row.ReporterConfirmed);
            Assert.Equal("Still cuts out after an hour.", row.ReporterComment);
            Assert.NotNull(row.ReporterRespondedAt);
            Assert.Equal(row.ReporterRespondedAt, row.AgentQueuedAt);
        }

        // One answer. The second is a 409 and the first stands.
        var again = await reporter.PostAsJsonAsync(
            ConfirmUrl(check), new ReporterConfirmationDto(true, null), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Already answered", await again.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(VerificationStatus.Pending)]
    [InlineData(VerificationStatus.Expired)]
    [InlineData(VerificationStatus.Escalated)]
    public async Task Confirm_IsA409_WhenTheCheckIsNotWaitingOnTheReporter(VerificationStatus status)
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);
        var check = await SeedCheckAsync($"NA{(int)status}", reporterId, status);

        var response = await reporter.PostAsJsonAsync(
            ConfirmUrl(check), new ReporterConfirmationDto(true, null), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Not awaiting a response", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Confirm_IsABoundedForm_AMissingAnswerIsNotANo_AndTheCommentStopsAt300()
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);
        var check = await SeedCheckAsync("BND", reporterId, VerificationStatus.AwaitingReporterResponse);

        // A plain bool would bind this as false and reopen a repair nobody said had failed.
        var missing = await reporter.PostAsJsonAsync(ConfirmUrl(check), new { comment = "hm" }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var tooLong = await reporter.PostAsJsonAsync(
            ConfirmUrl(check), new ReporterConfirmationDto(true, new string('x', 301)), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        // Nothing was recorded by either refusal, so the cap itself is still answerable.
        var atTheCap = await reporter.PostAsJsonAsync(
            ConfirmUrl(check), new ReporterConfirmationDto(true, new string('x', 300)), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, atTheCap.StatusCode);
    }

    [Fact]
    public async Task List_AReporterSeesOnlyChecksOnTheirOwnReports_AManagerSeesThemAll()
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);
        var (_, otherId) = await ClientForAsync(Role.Reporter);
        var (manager, _) = await ClientForAsync(Role.FacilitiesManager);

        var mine = await SeedCheckAsync("LS1", reporterId, VerificationStatus.AwaitingReporterResponse);
        var theirs = await SeedCheckAsync("LS2", otherId, VerificationStatus.AwaitingReporterResponse);

        var page = await GetPageAsync(reporter, "/api/verifications?pageSize=100");
        Assert.Equal(new[] { mine }, page.Items.Select(i => i.Id));
        Assert.Equal(1, page.TotalCount);

        // Filtering cannot widen the scope — asking for the other asset returns nothing.
        var theirAsset = await AssetOfAsync(theirs);
        var widened = await GetPageAsync(reporter, $"/api/verifications?assetId={theirAsset}");
        Assert.Empty(widened.Items);

        // A manager sees both, and the asset filter is exact.
        var managerView = await GetPageAsync(manager, "/api/verifications?pageSize=100");
        Assert.Contains(managerView.Items, i => i.Id == mine);
        Assert.Contains(managerView.Items, i => i.Id == theirs);

        var byAsset = await GetPageAsync(manager, $"/api/verifications?assetId={theirAsset}");
        Assert.Equal(new[] { theirs }, byAsset.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_FiltersByStatusAndByDueDate_BothEndsInclusive()
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);

        var lastMinute = await SeedCheckAsync("DT1", reporterId, VerificationStatus.Confirmed,
            dueAt: new DateTime(2026, 3, 10, 23, 59, 0, DateTimeKind.Utc));
        var nextDay = await SeedCheckAsync("DT2", reporterId, VerificationStatus.AwaitingReporterResponse,
            dueAt: new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc));

        var upTo10th = await GetPageAsync(reporter, "/api/verifications?dateTo=2026-03-10");
        Assert.Equal(new[] { lastMinute }, upTo10th.Items.Select(i => i.Id));

        var from11th = await GetPageAsync(reporter, "/api/verifications?dateFrom=2026-03-11&dateTo=2026-03-11");
        Assert.Equal(new[] { nextDay }, from11th.Items.Select(i => i.Id));

        // Default sort is latest due first.
        var both = await GetPageAsync(reporter, "/api/verifications");
        Assert.Equal(new[] { nextDay, lastMinute }, both.Items.Select(i => i.Id));

        var confirmed = await GetPageAsync(reporter, "/api/verifications?status=Confirmed");
        Assert.Equal(new[] { lastMinute }, confirmed.Items.Select(i => i.Id));

        // By NAME: an unknown status is a 400, not a filter that matches nothing.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await reporter.GetAsync("/api/verifications?status=Fixed")).StatusCode);
    }

    [Fact]
    public async Task Detail_CarriesTheWorkOrdersClaim_AndIsScopedLikeTheList()
    {
        var (reporter, reporterId) = await ClientForAsync(Role.Reporter);
        var (stranger, _) = await ClientForAsync(Role.Reporter);
        var (manager, _) = await ClientForAsync(Role.FacilitiesManager);

        var check = await SeedCheckAsync("DTL", reporterId, VerificationStatus.AwaitingReporterResponse);

        var response = await reporter.GetAsync($"/api/verifications/{check}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<VerificationDetailDto>(JsonOptions);
        Assert.Equal("filter cleaned, tested 30min.", detail!.WorkOrderResolutionNote);
        Assert.NotNull(detail.WorkOrderCompletedAt);
        Assert.Equal("PRJ-DTL-01", detail.Asset.AssetTag);
        Assert.Null(detail.AgentOutcome);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/verifications/{check}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/verifications/{check}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reporter.GetAsync($"/api/verifications/{check + 10_000}")).StatusCode);
    }

    [Fact]
    public async Task Metrics_AreForAFacilitiesManager_401And403KeptApart()
    {
        var anonymous = await _factory.CreateClient().GetAsync("/api/analytics/metrics");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Admin included — the policy names one role.
        foreach (var role in new[] { Role.Reporter, Role.Technician, Role.Admin })
        {
            var (client, _) = await ClientForAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/analytics/metrics")).StatusCode);
        }

        var (manager, _) = await ClientForAsync(Role.FacilitiesManager);
        var response = await manager.GetAsync("/api/analytics/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<MetricsDto>(JsonOptions));
    }

    // ---------------------------------------------------------------------------

    private static string ConfirmUrl(int checkId) => $"/api/verifications/{checkId}/confirm";

    private static async Task<PagedResult<VerificationCheckDto>> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<VerificationCheckDto>>(JsonOptions))!;
    }

    /// <summary>A completed work order and its check, on a report filed by <paramref name="reporterId"/>.</summary>
    private async Task<int> SeedCheckAsync(
        string prefix,
        int reporterId,
        VerificationStatus status,
        DateTime? dueAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var due = dueAt ?? DateTime.UtcNow.AddDays(-1);
        var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, prefix, due.AddDays(-5));

        var report = await db.Reports.SingleAsync(r => r.Id == order.ReportId);
        report.ReporterId = reporterId;

        var check = new VerificationCheck
        {
            WorkOrderId = order.Id,
            AssetId = order.AssetId,
            DueAt = due,
            Status = status,
            ProcessedAt = status == VerificationStatus.Pending ? null : due
        };

        db.VerificationChecks.Add(check);
        await db.SaveChangesAsync();
        return check.Id;
    }

    private async Task<int> AssetOfAsync(int checkId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.VerificationChecks.Where(v => v.Id == checkId).Select(v => v.AssetId).SingleAsync();
    }

    private async Task<(HttpClient Client, int UserId)> ClientForAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "VerifyPass1", "Test User", role),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }
}
