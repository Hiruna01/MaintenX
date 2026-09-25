using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// The approval gate and the manager's decisions — the cases WorkOrderEndpointTests does not
/// already pin. That class has under / over / a cent over the threshold, EscalateReplacement
/// at Rs 100, a Technician's 403, the 409 for a second approval, reject without a reason and
/// request-revision back to Strategizing. What is added here:
///
///   * EXACTLY ON THE THRESHOLD, FOR EVERY STRATEGY. The rule is "strictly ABOVE": an
///     estimate equal to Approval:CostThreshold does NOT need a manager — unless it is a
///     replacement, which always does. Both halves at the one value where they diverge.
///   * EscalateReplacement at every cost that matters, Rs 0 included.
///   * No token is 401 on every decision, not only on create.
///   * A decision, once made, is not rewritten by the opposite one.
/// </summary>
public class ApprovalTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApprovalTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const decimal Threshold = ApprovalSettings.DefaultCostThreshold;

    // ---------------------------------------------------------------------------------
    // The boundary
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// COST == THRESHOLD DOES NOT REQUIRE APPROVAL. The threshold is the most that may be
    /// spent without a manager, so an estimate sitting exactly on it is inside the limit and
    /// is approved on the spot. Only EscalateReplacement goes to a manager at this cost — for
    /// being a replacement, not for the money. The approval basis says the same thing the
    /// status does: not above the threshold.
    /// </summary>
    [Theory]
    [InlineData(WorkOrderStrategy.KnownFix, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.SingleJob, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.ConsolidatedJob, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.InspectFirst, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.Defer, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.EscalateReplacement, WorkOrderStatus.AwaitingApproval)]
    public async Task AtExactlyTheThreshold_OnlyAReplacementNeedsAManager(
        WorkOrderStrategy strategy, WorkOrderStatus expected)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var order = await RaiseAsync(manager, await NewFaultAsync(), Threshold, strategy);

        Assert.Equal(expected, order.Status);

        var basis = (await DetailAsync(manager, order.Id)).ApprovalBasis;
        Assert.Equal(Threshold, basis.Threshold);
        Assert.False(basis.ExceedsThreshold);
        Assert.Equal(strategy == WorkOrderStrategy.EscalateReplacement, basis.RequiresApproval);
    }

    /// <summary>
    /// A replacement needs a manager whatever it costs — free, a cent under the threshold, on
    /// it, or over it. Replacing equipment is a decision about the estate, not only about money.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("14999.99")]
    [InlineData("15000")]
    [InlineData("15000.01")]
    [InlineData("250000")]
    public async Task EscalateReplacement_NeedsAManager_AtAnyCost(string cost)
    {
        // Strings, because an attribute cannot hold a decimal — and never via a double.
        var estimate = decimal.Parse(cost, CultureInfo.InvariantCulture);
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        var order = await RaiseAsync(manager, fault, estimate, WorkOrderStrategy.EscalateReplacement);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, order.Status);
        Assert.True((await DetailAsync(manager, order.Id)).ApprovalBasis.IsReplacement);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, await WorkflowStateAsync(fault.ReportId));
    }

    // ---------------------------------------------------------------------------------
    // Who may decide
    // ---------------------------------------------------------------------------------

    /// <summary>No token is 401 — "who are you?" — on every decision, and nothing moves.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-revision")]
    public async Task ManagerDecisions_WithNoToken_Are401(string action)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/api/workorders/{order.Id}/{action}",
            new { reason = "No reason.", note = "No note." }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(WorkOrderStatus.AwaitingApproval, (await DetailAsync(manager, order.Id)).Status);
    }

    // ---------------------------------------------------------------------------------
    // A decision stands
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ARejectedOrder_CannotThenBeApprovedOrSentForRevision()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        var reject = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Out of budget this term."), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var approve = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);
        var revise = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/request-revision",
            new RequestRevisionDto("Try a cheaper fix."), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, revise.StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(WorkOrderStatus.Rejected, detail.Status);
        Assert.Equal("Out of budget this term.", detail.RejectionReason);
    }

    [Fact]
    public async Task AnApprovedOrder_CannotThenBeRejected()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PostAsync($"/api/workorders/{order.Id}/approve", null)).StatusCode);

        var reject = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Changed my mind."), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(WorkOrderStatus.Approved, detail.Status);
        Assert.Null(detail.RejectionReason);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private record Fault(int ReportId, int AssetId);

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "ApprovalPass1", "Test User", role),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>A room, an asset in it, and a report filed through POST /api/reports.</summary>
    private async Task<Fault> NewFaultAsync()
    {
        var anonymous = _factory.CreateClient();

        var building = await (await anonymous.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await anonymous.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var (admin, _) = await ClientAsync(Role.Admin);

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

    private static async Task<WorkOrderDto> RaiseAsync(
        HttpClient manager,
        Fault fault,
        decimal estimatedCost,
        WorkOrderStrategy strategy = WorkOrderStrategy.SingleJob)
    {
        var response = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(fault.ReportId, fault.AssetId, strategy, estimatedCost, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions))!;
    }

    private static async Task<WorkOrderDetailDto> DetailAsync(HttpClient manager, int orderId) =>
        (await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions))!;

    private async Task<WorkflowState> WorkflowStateAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.CurrentState).SingleAsync();
    }
}
