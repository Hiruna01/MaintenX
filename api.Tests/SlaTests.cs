using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Xunit;

namespace api.Tests;

/// <summary>
/// The repair SLA — SlaRules and where WorkOrderService stamps it.
///
///   * The rule as pure functions, on its boundaries to the tick: exactly ON the due time is
///     still on time, one tick after is overdue (live) or missed (completed).
///   * The clock starts at APPROVAL: by the gate for an order inside the threshold, by the
///     manager's decision for one above it — never at raise time for an order that waited.
///   * No clock for an order waiting on a manager, or one rejected.
///   * The list row and the detail read the same verdict, against the injected clock.
/// </summary>
public class SlaTests : IClassFixture<SweepClockApiFactory>
{
    private readonly SweepClockApiFactory _factory;

    public SlaTests(SweepClockApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly TimeSpan Sla = TimeSpan.FromDays(SlaSettings.DefaultResolutionDays);

    // ---------------------------------------------------------------------------------
    // SlaRules, as pure functions
    // ---------------------------------------------------------------------------------

    private static readonly DateTime Due = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DueAt_IsApprovalPlusTheResolutionDays()
    {
        var approved = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc), SlaRules.DueAt(approved, 7));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.InProgress)]
    public void ALiveOrder_IsOnTrackUpToItsDueTime_AndOverdueOneTickAfter(WorkOrderStatus status)
    {
        Assert.Equal(SlaState.OnTrack, SlaRules.StateOf(status, Due, null, Due.AddDays(-1)));
        Assert.Equal(SlaState.OnTrack, SlaRules.StateOf(status, Due, null, Due));
        Assert.Equal(SlaState.Overdue, SlaRules.StateOf(status, Due, null, Due.AddTicks(1)));
    }

    [Fact]
    public void ACompletedOrder_MetItsSla_AtOrBeforeTheDueTime_AndMissedItAfter()
    {
        // "now" is irrelevant once the job is done: a met SLA does not turn missed later.
        var later = Due.AddYears(1);

        Assert.Equal(SlaState.Met, SlaRules.StateOf(WorkOrderStatus.Completed, Due, Due.AddHours(-3), later));
        Assert.Equal(SlaState.Met, SlaRules.StateOf(WorkOrderStatus.Completed, Due, Due, later));
        Assert.Equal(SlaState.Missed, SlaRules.StateOf(WorkOrderStatus.Completed, Due, Due.AddTicks(1), later));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.AwaitingApproval)]
    [InlineData(WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStatus.Completed)]
    public void NoDueTime_IsNoClock_NotOnTrack(WorkOrderStatus status)
    {
        Assert.Equal(SlaState.None, SlaRules.StateOf(status, null, Due, Due.AddDays(30)));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Rejected)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void AnOrderThatWillNeverBeDone_HasNothingToBeLateFor(WorkOrderStatus status)
    {
        Assert.Equal(SlaState.None, SlaRules.StateOf(status, Due, null, Due.AddDays(30)));
    }

    // ---------------------------------------------------------------------------------
    // Through the endpoints
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AnOrderApprovedByTheGate_StartsItsSlaWhenRaised()
    {
        var raisedAt = _factory.Clock.Now = SweepClockApiFactory.Start;
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var order = await RaiseAsync(manager, await NewFaultAsync(), 1_000m);

        Assert.Equal(WorkOrderStatus.Approved, order.Status);
        Assert.Equal(raisedAt.UtcDateTime + Sla, order.DueAt);
        Assert.Equal(SlaState.OnTrack, order.Sla);
    }

    [Fact]
    public async Task AnOrderWaitingOnAManager_HasNoClock_UntilTheManagerApproves_ThenItStartsThere()
    {
        _factory.Clock.Now = SweepClockApiFactory.Start;
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, order.Status);
        Assert.Null(order.DueAt);
        Assert.Equal(SlaState.None, order.Sla);

        // Decided two days later: the SLA counts from the decision, not from the raise.
        var decidedAt = _factory.Clock.Now = SweepClockApiFactory.Start.AddDays(2);
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PostAsync($"/api/workorders/{order.Id}/approve", null)).StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(decidedAt.UtcDateTime + Sla, detail.DueAt);
        Assert.Equal(SlaState.OnTrack, detail.Sla);
    }

    [Fact]
    public async Task ARejectedOrder_NeverStartsAClock()
    {
        _factory.Clock.Now = SweepClockApiFactory.Start;
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync(
            $"/api/workorders/{order.Id}/reject", new RejectWorkOrderDto("Out of budget."), JsonOptions)).StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Null(detail.DueAt);
        Assert.Equal(SlaState.None, detail.Sla);
    }

    /// <summary>
    /// The list row and the detail agree, and both read the injected clock: on track at the
    /// due minute, overdue the minute after.
    /// </summary>
    [Fact]
    public async Task ALiveOrder_TurnsOverdue_TheMinuteAfterItsDueTime_OnTheListAndTheDetail()
    {
        _factory.Clock.Now = SweepClockApiFactory.Start;
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 1_000m);
        var dueAt = order.DueAt!.Value;

        _factory.Clock.Now = new DateTimeOffset(dueAt, TimeSpan.Zero);
        Assert.Equal(SlaState.OnTrack, (await DetailAsync(manager, order.Id)).Sla);
        Assert.Equal(SlaState.OnTrack, (await ListRowAsync(manager, fault, order.Id)).Sla);

        _factory.Clock.Now = new DateTimeOffset(dueAt.AddMinutes(1), TimeSpan.Zero);
        Assert.Equal(SlaState.Overdue, (await DetailAsync(manager, order.Id)).Sla);
        Assert.Equal(SlaState.Overdue, (await ListRowAsync(manager, fault, order.Id)).Sla);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private record Fault(int ReportId, int AssetId);

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await _factory.RegisterAsync(new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SlaPass123", "Test User", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>A room, an asset in it, and a report filed through POST /api/reports.</summary>
    private async Task<Fault> NewFaultAsync()
    {
        var admin = await _factory.CreateAdminClientAsync();

        var building = await (await admin.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await admin.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var asset = await (await admin.PostAsJsonAsync(
                "/api/assets",
                new CreateAssetDto(UniqueCode(), "Ceiling Projector", category!.Id, room!.Id, "Acme", "X1",
                    new DateOnly(2024, 1, 15), null),
                JsonOptions))
            .Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        var (reporter, _) = await ClientAsync(Role.Reporter);
        var report = await (await reporter.PostAsJsonAsync(
                "/api/reports", new CreateReportDto("Projector keeps cutting out mid-lecture.", room.Id), JsonOptions))
            .Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        // Where a finished agent run leaves it: a work order is raised from Strategizing.
        await WorkflowTestData.ReadyForWorkOrderAsync(_factory.Services, report!.Id);

        return new Fault(report.Id, asset!.Id);
    }

    private static async Task<WorkOrderDto> RaiseAsync(HttpClient manager, Fault fault, decimal estimatedCost)
    {
        var response = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(fault.ReportId, fault.AssetId, WorkOrderStrategy.SingleJob, estimatedCost, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions))!;
    }

    private static async Task<WorkOrderDetailDto> DetailAsync(HttpClient manager, int orderId) =>
        (await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions))!;

    private static async Task<WorkOrderDto> ListRowAsync(HttpClient manager, Fault fault, int orderId)
    {
        var page = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?assetId={fault.AssetId}", JsonOptions);

        return Assert.Single(page!.Items, w => w.Id == orderId);
    }
}
