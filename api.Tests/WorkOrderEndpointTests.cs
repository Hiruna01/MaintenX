using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
/// The work order endpoints, end to end. What is pinned here is the part that would be
/// expensive to get wrong quietly:
///
///   * THE APPROVAL GATE, at its boundary — exactly on the threshold is not above it — and
///     for the strategy that always needs a manager whatever it costs. Plus the hole a
///     non-nullable DTO would leave: an estimate that is simply missing must be a 400, not a
///     free job that sails through.
///   * 401 vs 403, and a Technician refused on every manager decision.
///   * COMPLETION IS ONE TRANSACTION. The happy path writes four things; a failure after the
///     first SaveChanges must leave none of them behind.
///   * Visibility is the service's rule, and the cost sort is decimal, not text.
/// </summary>
public class WorkOrderEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkOrderEndpointTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const decimal Threshold = ApprovalSettings.DefaultCostThreshold;

    private const string Note = "Cleaned filter, fan brg noisy. Temp fix, recommend replacement.";

    // ---------------------------------------------------------------------------------
    // The approval gate
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_UnderTheThreshold_IsApprovedAndTheWorkflowRaisesTheOrder()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        var response = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(fault.ReportId, fault.AssetId, WorkOrderStrategy.SingleJob, 4_500m, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);

        Assert.Equal(WorkOrderStatus.Approved, created!.Status);
        Assert.Equal(4_500m, created.EstimatedCost);
        Assert.Null(created.AssignedTechnicianId);
        Assert.Equal(WorkflowState.WorkOrderRaised, await WorkflowStateAsync(fault.ReportId));

        // CreatedAtAction must point at a GET that actually serves it. Auto-approved means
        // nobody decided, so there is no approver — null, not "waiting".
        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>(response.Headers.Location, JsonOptions);
        Assert.Equal(created.Id, detail!.Id);
        Assert.Null(detail.ApprovedBy);
        Assert.Null(detail.ApprovedAt);
    }

    [Fact]
    public async Task Create_ExactlyOnTheThreshold_IsNotAboveIt()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var onIt = await RaiseAsync(manager, await NewFaultAsync(), Threshold);
        var aCentOver = await RaiseAsync(manager, await NewFaultAsync(), Threshold + 0.01m);

        Assert.Equal(WorkOrderStatus.Approved, onIt.Status);
        Assert.Equal(WorkOrderStatus.AwaitingApproval, aCentOver.Status);
    }

    [Fact]
    public async Task Create_OverTheThreshold_WaitsForAManager()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        var created = await RaiseAsync(manager, fault, 42_000m);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, created.Status);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, await WorkflowStateAsync(fault.ReportId));
    }

    [Fact]
    public async Task Create_EscalateReplacement_WaitsForAManagerHoweverCheap()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var created = await RaiseAsync(manager, await NewFaultAsync(), 100m, WorkOrderStrategy.EscalateReplacement);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, created.Status);
    }

    [Theory]
    [InlineData("estimatedCost")]
    [InlineData("strategy")]
    public async Task Create_WithAGateInputMissing_Returns400RatherThanDefaultingIt(string missing)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        // Hand-rolled so the field can be left out entirely. A plain decimal would bind this
        // as 0 — under any threshold — and auto-approve an order nobody costed.
        var fields = new Dictionary<string, object>
        {
            ["reportId"] = fault.ReportId,
            ["assetId"] = fault.AssetId,
            ["strategy"] = "SingleJob",
            ["estimatedCost"] = 99_000m
        };
        fields.Remove(missing);

        var response = await manager.PostAsync(
            "/api/workorders",
            new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountOrdersForReportAsync(fault.ReportId));
    }

    [Fact]
    public async Task Create_ForAnUnknownReportOrAsset_Returns400()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        var noReport = await manager.PostAsJsonAsync("/api/workorders",
            new CreateWorkOrderDto(999_999, fault.AssetId, WorkOrderStrategy.SingleJob, 10m, null), JsonOptions);
        var noAsset = await manager.PostAsJsonAsync("/api/workorders",
            new CreateWorkOrderDto(fault.ReportId, 999_999, WorkOrderStrategy.SingleJob, 10m, null), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, noReport.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noAsset.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoToken_Is401_AndAsATechnician_Is403()
    {
        var (technician, _) = await ClientAsync(Role.Technician);
        var fault = await NewFaultAsync();
        var body = new CreateWorkOrderDto(fault.ReportId, fault.AssetId, WorkOrderStrategy.SingleJob, 10m, null);

        var anonymous = await _factory.CreateClient().PostAsJsonAsync("/api/workorders", body, JsonOptions);
        var wrongRole = await technician.PostAsJsonAsync("/api/workorders", body, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Approve / reject / request-revision
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_RecordsTheManager_AndASecondApprovalIs409()
    {
        var (manager, managerId) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        var first = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);
        var second = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        Assert.Equal(WorkOrderStatus.Approved, detail!.Status);
        Assert.Equal(managerId, detail.ApprovedBy!.Id);
        Assert.NotNull(detail.ApprovedAt);
        Assert.Equal(WorkflowState.WorkOrderRaised, await WorkflowStateAsync(fault.ReportId));
    }

    [Fact]
    public async Task Approve_AnOrderThatNeverNeededApproval_Is409()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 10m);

        var response = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reject_WithoutAReason_Is400_AndWithOne_ClosesTheWorkflow()
    {
        var (manager, managerId) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        var missing = await manager.PostAsync($"/api/workorders/{order.Id}/reject",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var blank = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("   "), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var rejected = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Out of budget this term; defer to January."), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        Assert.Equal(WorkOrderStatus.Rejected, detail!.Status);
        Assert.Equal("Out of budget this term; defer to January.", detail.RejectionReason);
        Assert.Equal(managerId, detail.ApprovedBy!.Id);
        Assert.Equal(WorkflowState.Closed, await WorkflowStateAsync(fault.ReportId));
    }

    [Fact]
    public async Task RequestRevision_SendsTheOrderBackAndRequeuesTheWorkflow()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);
        var workflowId = await WorkflowIdAsync(fault.ReportId);

        // Filing the report queued its own workflow; clear that so the only id left is the
        // one the revision queues. The runner is removed in tests, so nothing else reads it.
        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();
        await DrainAsync(queue);

        var response = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/request-revision",
            new RequestRevisionDto("Quote a fan and filter swap before replacing the unit."), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        Assert.Equal(WorkOrderStatus.Draft, detail!.Status);
        Assert.Equal("Quote a fan and filter swap before replacing the unit.", detail.RevisionNote);
        Assert.Equal(WorkflowState.Strategizing, await WorkflowStateAsync(fault.ReportId));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Equal(workflowId, await queue.DequeueAsync(timeout.Token));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-revision")]
    public async Task ManagerDecisions_AsATechnician_Are403(string action)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, _) = await ClientAsync(Role.Technician);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        // A valid body for every action, so the 403 cannot be a 400 in disguise.
        var response = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/{action}",
            new { reason = "Because I said so.", note = "Because I said so." }, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        Assert.Equal(WorkOrderStatus.AwaitingApproval, detail!.Status);
    }

    // ---------------------------------------------------------------------------------
    // Assign and complete
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Assign_OnlyToATechnician_AndOnlyOnceApproved()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var (_, reporterId) = await ClientAsync(Role.Reporter);

        var waiting = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);
        var approved = await RaiseAsync(manager, await NewFaultAsync(), 10m);

        var beforeApproval = await manager.PutAsJsonAsync($"/api/workorders/{waiting.Id}/assign",
            new AssignTechnicianDto(technicianId), JsonOptions);
        var toAReporter = await manager.PutAsJsonAsync($"/api/workorders/{approved.Id}/assign",
            new AssignTechnicianDto(reporterId), JsonOptions);
        var ok = await manager.PutAsJsonAsync($"/api/workorders/{approved.Id}/assign",
            new AssignTechnicianDto(technicianId), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, beforeApproval.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toAReporter.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{approved.Id}", JsonOptions);
        Assert.Equal(technicianId, detail!.AssignedTechnician!.Id);
    }

    [Fact]
    public async Task Complete_WritesTheOrderTheServiceRecordTheCheckAndTheWorkflowTogether()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician, "Nimal Perera");
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 10m);
        await AssignAsync(manager, order.Id, technicianId);

        var response = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(1_250.50m, ServiceOutcome.TemporaryFix, Note, null), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == order.Id);
        Assert.Equal(WorkOrderStatus.Completed, stored.Status);
        Assert.Equal(1_250.50m, stored.ActualCost);
        Assert.Equal(Note, stored.ResolutionNote);
        Assert.NotNull(stored.CompletedAt);

        // The history row the diagnostic agent will read: against the right asset, pointing
        // back at this order, with the note verbatim and the outcome as the technician said.
        var record = await db.ServiceRecords.AsNoTracking().SingleAsync(s => s.WorkOrderId == order.Id);
        Assert.Equal(fault.AssetId, record.AssetId);
        Assert.Equal("Nimal Perera", record.TechnicianName);
        Assert.Equal(Note, record.TechnicianNote);
        Assert.Equal(ServiceOutcome.TemporaryFix, record.Outcome);
        Assert.Equal(DateOnly.FromDateTime(stored.CompletedAt!.Value), record.ServicedOn);

        var check = await db.VerificationChecks.AsNoTracking().SingleAsync(v => v.WorkOrderId == order.Id);
        Assert.Equal(VerificationStatus.Pending, check.Status);

        Assert.Equal(WorkflowState.AwaitingVerification, await WorkflowStateAsync(fault.ReportId));

        // Completed is the end of the order's life as live work.
        var again = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(1_250.50m, ServiceOutcome.TemporaryFix, Note, null), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Complete_WithANoteUnderTwentyCharacters_Is400_AndWritesNoHistory()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        await AssignAsync(manager, order.Id, technicianId);

        // "done" is what the floor exists to refuse; nineteen characters is its boundary.
        var done = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, "done", null), JsonOptions);
        var nineteen = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, new string('x', 19), null), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, done.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nineteen.StatusCode);
        Assert.Equal(0, await CountServiceRecordsForOrderAsync(order.Id));

        var twenty = await technician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete",
            new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, new string('x', 20), null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, twenty.StatusCode);
    }

    [Fact]
    public async Task Complete_ByAnyoneButTheAssignedTechnician_Is403()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, assigneeId) = await ClientAsync(Role.Technician);
        var (otherTechnician, _) = await ClientAsync(Role.Technician);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        await AssignAsync(manager, order.Id, assigneeId);

        var body = new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, Note, null);

        var byOther = await otherTechnician.PostAsJsonAsync($"/api/workorders/{order.Id}/complete", body, JsonOptions);
        var byManager = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/complete", body, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode);
        Assert.Equal(0, await CountServiceRecordsForOrderAsync(order.Id));
    }

    [Fact]
    public async Task Complete_WhenAWriteFailsAfterTheFirstSave_LeavesNothingBehind()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 10m);
        await AssignAsync(manager, order.Id, technicianId);

        // The real service against the real database, with the verification step made to
        // fail — which happens AFTER the order and the service record have been saved. Only
        // the transaction stands between that and a half-completed order; without it this
        // test fails with a ServiceRecord on the asset and a Completed order.
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var service = new WorkOrderService(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<ApprovalSettings>(),
                sp.GetRequiredService<IWorkflowQueue>(),
                new FailingVerificationService(),
                TimeProvider.System,
                sp.GetRequiredService<SchedulingSettings>(),
                sp.GetRequiredService<IAssetService>(),
                sp.GetRequiredService<IFileStorageService>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
                order.Id, technicianId,
                new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, Note, null)));
        }

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == order.Id);
        Assert.Equal(WorkOrderStatus.Approved, stored.Status);
        Assert.Null(stored.CompletedAt);
        Assert.Equal(0, await CountServiceRecordsForOrderAsync(order.Id));
        Assert.Equal(WorkflowState.WorkOrderRaised, await WorkflowStateAsync(fault.ReportId));
    }

    // ---------------------------------------------------------------------------------
    // Reads — visibility and sorting
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task List_ATechnicianSeesOnlyTheirOwn_AndCannotReadAnotherByIdEither()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (mine, myId) = await ClientAsync(Role.Technician);
        var (_, theirId) = await ClientAsync(Role.Technician);
        var (reporter, _) = await ClientAsync(Role.Reporter);

        var myOrder = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        var theirOrder = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        await AssignAsync(manager, myOrder.Id, myId);
        await AssignAsync(manager, theirOrder.Id, theirId);

        // Even naming the other technician outright does not widen the scope.
        var page = await mine.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?technicianId={theirId}", JsonOptions);
        var own = await mine.GetFromJsonAsync<PagedResult<WorkOrderDto>>("/api/workorders", JsonOptions);

        Assert.Empty(page!.Items);
        Assert.Equal(myOrder.Id, Assert.Single(own!.Items).Id);
        Assert.Equal(1, own.TotalCount);

        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync($"/api/workorders/{myOrder.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.GetAsync($"/api/workorders/{theirOrder.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.GetAsync("/api/workorders/999999")).StatusCode);

        // A reporter is assigned nothing, so sees nothing — the rule fails closed.
        var reporterPage = await reporter.GetFromJsonAsync<PagedResult<WorkOrderDto>>("/api/workorders", JsonOptions);
        Assert.Empty(reporterPage!.Items);

        // And a manager sees the estate, filterable by technician.
        var managerPage = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?technicianId={theirId}", JsonOptions);
        Assert.Equal(theirOrder.Id, Assert.Single(managerPage!.Items).Id);
    }

    [Fact]
    public async Task Detail_ForTheAssignedTechnician_CarriesTheRoomAndTheDiagnosis()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 10m);
        await AssignAsync(manager, order.Id, technicianId);

        // Nothing recorded yet: null, not an empty diagnosis.
        var before = await technician.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        Assert.Null(before!.Diagnosis);
        Assert.Equal(await RoomOfAsync(fault.AssetId), before.Room.Id);
        Assert.Equal("Lecture Hall A", before.Room.Name);

        using (var scope = _factory.Services.CreateScope())
        {
            var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
            Assert.True(await workflows.RecordStepAsync(
                await WorkflowIdAsync(fault.ReportId), AgentRunResponse.DiagnosticAgentName, "[]",
                """
                {"hypotheses":[{"cause":"Overheating due to a failing cooling fan","confidence":"high",
                  "evidence":["2026-09-02: fan bearing weak, temporary fix."]}],
                 "primary_hypothesis_index":0,"recommended_next_action":"replace",
                 "reasoning_summary":"Cleaning is not holding."}
                """,
                0, "Ok", null));
        }

        var after = await technician.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);
        var hypothesis = Assert.Single(after!.Diagnosis!.Hypotheses);
        Assert.Equal("Overheating due to a failing cooling fan", hypothesis.Cause);
        Assert.Equal("2026-09-02: fan bearing weak, temporary fix.", Assert.Single(hypothesis.Evidence));
        Assert.Equal("replace", after.Diagnosis.RecommendedNextAction);
    }

    [Fact]
    public async Task List_SortedByCost_OrdersAsMoneyNotAsText()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        // Compared as strings, "9000.00" sorts above "10000.00" and "950.00" above both —
        // exactly what SQLite would do with a decimal stored as TEXT.
        await RaiseAsync(manager, fault, 9_000m);
        await RaiseAsync(manager, fault, 10_000m);
        await RaiseAsync(manager, fault, 950m);

        var page = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?assetId={fault.AssetId}&sort=Cost", JsonOptions);

        Assert.Equal(new[] { 10_000m, 9_000m, 950m }, page!.Items.Select(w => w.EstimatedCost));
    }

    [Fact]
    public async Task List_FiltersByStatus_AndRejectsAnUnknownOne()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var waiting = await RaiseAsync(manager, fault, 42_000m);
        await RaiseAsync(manager, fault, 10m);

        var page = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?assetId={fault.AssetId}&status=AwaitingApproval", JsonOptions);
        var unknown = await manager.GetAsync("/api/workorders?status=Pending");

        Assert.Equal(waiting.Id, Assert.Single(page!.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Slots — offering and booking
    //
    // The boundary arithmetic is unit tested in SlotRulesTests. These pin the wiring: that
    // the room comes from the asset, the technician's other visits are loaded, the dates
    // are read as campus-local days, and a booking re-checks rather than trusting an offer.
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AvailableSlots_AvoidTheBufferedClass_AndTheTechniciansOtherVisit()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var day = FutureMonday();

        var fault = await NewFaultAsync();
        await AddClassAsync(await RoomOfAsync(fault.AssetId), Local(day, 10), Local(day, 11));

        // The technician's other job, in a different room, booked through the endpoint.
        var other = await ApprovedAndAssignedAsync(manager, technicianId);
        var booked = await manager.PostAsJsonAsync($"/api/workorders/{other.Id}/schedule",
            new ScheduleWorkOrderDto(Local(day, 13), Local(day, 14)), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);

        var slots = await AvailableAsync(manager, fault.AssetId, day, day, 60, technicianId);

        // The class blocks 09:45-11:15; the visit blocks 13:00-14:00 with no buffer, so
        // 12:00-13:00 and 14:00-15:00 both survive.
        Assert.Equal(
            new[] { Local(day, 8), Local(day, 8, 30), Local(day, 11, 30), Local(day, 12),
                    Local(day, 14), Local(day, 14, 30), Local(day, 15), Local(day, 15, 30), Local(day, 16) },
            slots.Select(s => s.StartsAt));

        // Without a technician only the room is checked, so the afternoon opens up again.
        var roomOnly = await AvailableAsync(manager, fault.AssetId, day, day, 60, technicianId: null);
        Assert.Contains(Local(day, 13), roomOnly.Select(s => s.StartsAt));
    }

    [Fact]
    public async Task AvailableSlots_RefusesABadQuery_AndANonManager()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, _) = await ClientAsync(Role.Technician);
        var fault = await NewFaultAsync();
        var day = FutureMonday();

        string Url(string query) => $"/api/workorders/slots/available?{query}";
        var ok = $"assetId={fault.AssetId}&durationMinutes=60&fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}";

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(Url(ok))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technician.GetAsync(Url(ok))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url(ok))).StatusCode);

        var bad = new[]
        {
            $"assetId={fault.AssetId}&fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}",                        // no duration
            $"assetId=999999&durationMinutes=60&fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}",              // unknown asset
            $"assetId={fault.AssetId}&durationMinutes=60&fromDate={day:yyyy-MM-dd}&toDate={day.AddDays(-1):yyyy-MM-dd}", // backwards
            $"assetId={fault.AssetId}&durationMinutes=60&fromDate={day:yyyy-MM-dd}&toDate={day.AddDays(31):yyyy-MM-dd}", // 32 days
            $"assetId={fault.AssetId}&durationMinutes=600&fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}",    // longer than the day
        };

        foreach (var query in bad)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync(Url(query))).StatusCode);
        }
    }

    [Fact]
    public async Task Schedule_BooksAnOfferedSlot_AndTheOrderBecomesScheduled()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var order = await ApprovedAndAssignedAsync(manager, technicianId);
        var day = FutureMonday();

        var offered = (await AvailableAsync(manager, order.AssetId, day, day, 90, technicianId))[0];

        var response = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/schedule",
            new ScheduleWorkOrderDto(offered.StartsAt, offered.EndsAt), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>(response.Headers.Location, JsonOptions);
        Assert.Equal(WorkOrderStatus.Scheduled, detail!.Status);
        var slot = Assert.Single(detail.ScheduledSlots);
        Assert.Equal(offered.StartsAt, slot.StartsAt);
        Assert.Equal(offered.EndsAt, slot.EndsAt);
    }

    [Fact]
    public async Task Schedule_ASlotTakenSinceItWasOffered_Is409()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var first = await ApprovedAndAssignedAsync(manager, technicianId);
        var second = await ApprovedAndAssignedAsync(manager, technicianId);
        var day = FutureMonday();

        // Offered to the second order...
        var offered = (await AvailableAsync(manager, second.AssetId, day, day, 60, technicianId))[0];

        // ...then the same technician is booked into that time for the first order...
        var taken = await manager.PostAsJsonAsync($"/api/workorders/{first.Id}/schedule",
            new ScheduleWorkOrderDto(offered.StartsAt, offered.EndsAt), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, taken.StatusCode);

        // ...so booking the stale offer is refused rather than double-booking them.
        var stale = await manager.PostAsJsonAsync($"/api/workorders/{second.Id}/schedule",
            new ScheduleWorkOrderDto(offered.StartsAt, offered.EndsAt), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // The same for the room: a class synced in after the offer was made.
        var nextOffer = (await AvailableAsync(manager, second.AssetId, day, day, 60, technicianId))[0];
        await AddClassAsync(await RoomOfAsync(second.AssetId), nextOffer.StartsAt, nextOffer.EndsAt);

        var clashesWithClass = await manager.PostAsJsonAsync($"/api/workorders/{second.Id}/schedule",
            new ScheduleWorkOrderDto(nextOffer.StartsAt, nextOffer.EndsAt), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, clashesWithClass.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{second.Id}", JsonOptions);
        Assert.Equal(WorkOrderStatus.Approved, detail!.Status);
        Assert.Empty(detail.ScheduledSlots);
    }

    [Fact]
    public async Task Schedule_RefusesWhatCouldNeverHaveBeenOffered()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var order = await ApprovedAndAssignedAsync(manager, technicianId);
        var day = FutureMonday();

        async Task<HttpStatusCode> Book(DateTime start, DateTime end, HttpClient? caller = null) =>
            (await (caller ?? manager).PostAsJsonAsync($"/api/workorders/{order.Id}/schedule",
                new ScheduleWorkOrderDto(start, end), JsonOptions)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Book(Local(day, 7), Local(day, 8)));              // before opening
        Assert.Equal(HttpStatusCode.BadRequest, await Book(Local(day, 16, 30), Local(day, 17, 30)));   // past closing
        Assert.Equal(HttpStatusCode.BadRequest, await Book(Local(day.AddDays(5), 10), Local(day.AddDays(5), 11))); // Saturday
        Assert.Equal(HttpStatusCode.BadRequest, await Book(Local(day, 11), Local(day, 10)));           // backwards
        Assert.Equal(HttpStatusCode.Forbidden, await Book(Local(day, 10), Local(day, 11), technician));

        // No offset: which clock was that read off? Refused rather than guessed.
        var noOffset = await manager.PostAsync($"/api/workorders/{order.Id}/schedule", new StringContent(
            $$"""{"startsAt":"{{day:yyyy-MM-dd}}T10:00:00","endsAt":"{{day:yyyy-MM-dd}}T11:00:00"}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, noOffset.StatusCode);

        // Nobody assigned, or not yet approved: 409, whatever the slot.
        var unassigned = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        var waiting = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);
        foreach (var id in new[] { unassigned.Id, waiting.Id })
        {
            var response = await manager.PostAsJsonAsync($"/api/workorders/{id}/schedule",
                new ScheduleWorkOrderDto(Local(day, 10), Local(day, 11)), JsonOptions);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private record Fault(int ReportId, int AssetId);

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role, string fullName = "Test User")
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "WorkOrderPass1", fullName, role),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>
    /// A room, an asset in it, and a report filed against it through POST /api/reports —
    /// which is what raises the workflow the work order endpoints move.
    /// </summary>
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

        var assetResponse = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(UniqueCode(), "Ceiling Projector", category!.Id, room!.Id, "Acme", "X1",
                new DateOnly(2024, 1, 15), null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        var (reporter, _) = await ClientAsync(Role.Reporter);
        var reportResponse = await reporter.PostAsJsonAsync(
            "/api/reports", new CreateReportDto("Projector keeps cutting out mid-lecture.", room.Id), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        return new Fault(report!.Id, asset!.Id);
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

    private static async Task AssignAsync(HttpClient manager, int orderId, int technicianId)
    {
        var response = await manager.PutAsJsonAsync(
            $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(technicianId), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<int> WorkflowIdAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.Id).SingleAsync();
    }

    private async Task<WorkflowState> WorkflowStateAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.CurrentState).SingleAsync();
    }

    private async Task<int> CountOrdersForReportAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .WorkOrders.CountAsync(w => w.ReportId == reportId);
    }

    private async Task<int> CountServiceRecordsForOrderAsync(int orderId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ServiceRecords.CountAsync(s => s.WorkOrderId == orderId);
    }

    /// <summary>
    /// A Monday at least a week out, so no slot on it is ever "in the past" whenever the
    /// suite runs.
    /// </summary>
    private static DateOnly FutureMonday()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>A campus-local wall-clock time, as the UTC instant the API stores.</summary>
    private DateTime Local(DateOnly day, int hour, int minute = 0)
    {
        var zone = _factory.Services.GetRequiredService<SchedulingSettings>().TimeZone;
        return TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), zone);
    }

    private async Task<WorkOrderDto> ApprovedAndAssignedAsync(HttpClient manager, int technicianId)
    {
        var order = await RaiseAsync(manager, await NewFaultAsync(), 10m);
        await AssignAsync(manager, order.Id, technicianId);
        return order;
    }

    private static async Task<List<AvailableSlotDto>> AvailableAsync(
        HttpClient manager, int assetId, DateOnly from, DateOnly to, int durationMinutes, int? technicianId)
    {
        var url = $"/api/workorders/slots/available?assetId={assetId}&durationMinutes={durationMinutes}"
                + $"&fromDate={from:yyyy-MM-dd}&toDate={to:yyyy-MM-dd}"
                + (technicianId is null ? "" : $"&technicianId={technicianId}");

        return (await manager.GetFromJsonAsync<List<AvailableSlotDto>>(url, JsonOptions))!;
    }

    private async Task<int> RoomOfAsync(int assetId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Assets.Where(a => a.Id == assetId).Select(a => a.RoomId).SingleAsync();
    }

    /// <summary>
    /// A class in the room, written straight to the table — the timetable is mirrored from
    /// the campus system, and there is no endpoint that authors one.
    /// </summary>
    private async Task AddClassAsync(int roomId, DateTime startsAt, DateTime endsAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClassScheduleSlots.Add(new ClassScheduleSlot
        {
            RoomId = roomId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Title = "SE3090 Lecture",
            ExternalEventId = $"evt-{Guid.NewGuid():N}",
            SyncedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Empties the queue. DequeueAsync blocks, so a short timeout is the signal.</summary>
    private static async Task DrainAsync(IWorkflowQueue queue)
    {
        while (true)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            try
            {
                await queue.DequeueAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Fails the one call CompleteAsync makes, standing in for any write that goes wrong
    /// after the first SaveChanges. Nothing else is called on it.
    /// </summary>
    private sealed class FailingVerificationService : IVerificationService
    {
        public Task<VerificationCheckDto?> CreateForCompletedWorkOrderAsync(int workOrderId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure raising the verification check.");

        public Task<bool> HasOpenCheckAsync(int workOrderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<VerificationCheckDto>> GetAllAsync(int callerId, Role callerRole, string? search = null, VerificationStatus? status = null, int? assetId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, VerificationSort sort = VerificationSort.DueAt, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VerificationDetailDto?> GetDetailAsync(int id, int callerId, Role callerRole, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<VerificationCheckDto>> GetForWorkOrderAsync(int workOrderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VerificationSweepResultDto> ProcessDueChecksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConfirmVerificationOutcome> RecordReporterResponseAsync(int id, int callerId, ReporterConfirmationDto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VerificationMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
