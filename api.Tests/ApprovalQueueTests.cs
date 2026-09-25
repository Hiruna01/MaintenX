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
/// The approval queue, the approval basis on a work order, the diagnosis and proposal the
/// runner now keeps, and the technician list behind the dispatch board. What is pinned:
///
///   * THE BASIS IS THE GATE'S OWN READING. Above, on, and a cheap replacement — so a page
///     that renders "above the threshold" is repeating C#, not doing its own sum.
///   * NULL IS NOT EMPTY. No step recorded is a null diagnosis; a failed run is a diagnosis
///     that says it failed. And a diagnostic TOOL CALL, recorded under the same name, is
///     never mistaken for its answer.
///   * Money in the proposal is read as decimal from the JSON's own text.
///   * FacilitiesManager only, with 401 and 403 kept apart — an Admin refused as well.
/// </summary>
public class ApprovalQueueTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApprovalQueueTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const decimal Threshold = ApprovalSettings.DefaultCostThreshold;

    // ---------------------------------------------------------------------------------
    // Who may read it
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Queue_WithoutAToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/workorders/approvals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Technician)]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Admin)]
    public async Task Queue_ForAnyoneButAFacilitiesManager_Returns403(Role role)
    {
        var (client, _) = await ClientAsync(role);

        var response = await client.GetAsync("/api/workorders/approvals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // What is in it, and in what order
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Queue_HoldsOnlyOrdersAwaitingApproval_OldestFirst()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var first = await RaiseAsync(manager, await NewFaultAsync(), 40_000m);
        var autoApproved = await RaiseAsync(manager, await NewFaultAsync(), 900m);
        var second = await RaiseAsync(manager, await NewFaultAsync(), 60_000m);

        var ids = (await QueueAsync(manager)).Items.Select(c => c.WorkOrder.Id).ToList();

        Assert.DoesNotContain(autoApproved.Id, ids);
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);

        // A queue of decisions: the one that has waited longest comes first.
        Assert.True(ids.IndexOf(first.Id) < ids.IndexOf(second.Id));
        Assert.All((await QueueAsync(manager)).Items,
            c => Assert.Equal(WorkOrderStatus.AwaitingApproval, c.WorkOrder.Status));
    }

    [Fact]
    public async Task Queue_DropsAnOrderOnceItIsDecided()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 40_000m);

        var approve = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        Assert.DoesNotContain(order.Id, (await QueueAsync(manager)).Items.Select(c => c.WorkOrder.Id));
    }

    [Fact]
    public async Task Queue_CarriesTheAssetsHistoryOldestFirst_AndItsFailureSummary()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        // Inserted newest-first, so an oldest-first answer is the service ordering them and
        // not the table handing them back in insert order.
        await AddServiceRecordAsync(fault.AssetId, new DateOnly(2026, 9, 2), "fan brg weak, temp fix", ServiceOutcome.TemporaryFix);
        await AddServiceRecordAsync(fault.AssetId, new DateOnly(2026, 5, 12), "no fault found", ServiceOutcome.NoFaultFound);

        var order = await RaiseAsync(manager, fault, 45_000m, WorkOrderStrategy.EscalateReplacement);
        var item = await CaseAsync(manager, order.Id);

        Assert.Equal(fault.AssetId, item.Asset.Id);
        Assert.Equal(
            new[] { new DateOnly(2026, 5, 12), new DateOnly(2026, 9, 2) },
            item.Asset.ServiceHistory.Select(s => s.ServicedOn));

        Assert.Equal(fault.AssetId, item.FailureSummary.AssetId);
        Assert.Equal(1, item.FailureSummary.TemporaryFixCount);
    }

    // ---------------------------------------------------------------------------------
    // The approval basis — the gate explaining itself
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Basis_AboveTheThreshold_SaysSo_AndNamesTheThreshold()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 45_000m);

        var basis = (await CaseAsync(manager, order.Id)).WorkOrder.ApprovalBasis;

        Assert.Equal(Threshold, basis.Threshold);
        Assert.True(basis.ExceedsThreshold);
        Assert.False(basis.IsReplacement);
        Assert.True(basis.RequiresApproval);
    }

    [Fact]
    public async Task Basis_ACheapReplacement_NeedsApprovalWithoutExceedingTheThreshold()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 900m, WorkOrderStrategy.EscalateReplacement);

        var basis = (await CaseAsync(manager, order.Id)).WorkOrder.ApprovalBasis;

        Assert.False(basis.ExceedsThreshold);
        Assert.True(basis.IsReplacement);
        Assert.True(basis.RequiresApproval);
    }

    [Fact]
    public async Task Basis_OnTheDetailRead_ExactlyOnTheThresholdIsNotAboveIt()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), Threshold);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{order.Id}", JsonOptions);

        // The same function routed it (Approved) and explains it — they cannot disagree.
        Assert.Equal(WorkOrderStatus.Approved, detail!.Status);
        Assert.False(detail.ApprovalBasis.ExceedsThreshold);
        Assert.False(detail.ApprovalBasis.RequiresApproval);
    }

    // ---------------------------------------------------------------------------------
    // The diagnosis and the proposal, read back from the steps
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Case_WithNoAgentStepsRecorded_HasNullDiagnosisAndProposal()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 45_000m);

        var item = await CaseAsync(manager, order.Id);

        Assert.Null(item.Diagnosis);
        Assert.Null(item.Proposal);
    }

    [Fact]
    public async Task Case_ReadsTheRecordedDiagnosisAndProposal_AndIgnoresTheDiagnosticsToolCalls()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 45_000.50m, WorkOrderStrategy.EscalateReplacement);
        var workflowId = await WorkflowIdAsync(fault.ReportId);

        await AddStepAsync(workflowId, AgentRunResponse.DiagnosticAgentName, "[]", DiagnosisJson, "Ok");
        await AddStepAsync(workflowId, AgentRunResponse.StrategistAgentName, "[]", ProposalJson, "Ok");

        // A tool call the diagnostic made, recorded AFTER its answer and under the same
        // AgentName. The newest diagnostic-named row is therefore not the diagnosis — only
        // ToolCallsJson tells them apart.
        await AddStepAsync(workflowId, AgentRunResponse.DiagnosticAgentName,
            """[{"tool":"get_asset_service_history","arguments":{"id":1}}]""",
            """{"Tool":"get_asset_service_history","Found":true,"Result":[]}""", "Ok");

        var item = await CaseAsync(manager, order.Id);

        var diagnosis = Assert.IsType<AgentDiagnosisDto>(item.Diagnosis);
        Assert.True(diagnosis.OutputReadable);
        Assert.Equal(workflowId, diagnosis.WorkflowId);
        Assert.Equal(2, diagnosis.Hypotheses.Count);
        Assert.Equal(0, diagnosis.PrimaryHypothesisIndex);
        Assert.Equal("Overheating due to a failing cooling fan", diagnosis.Hypotheses[0].Cause);
        Assert.Equal("high", diagnosis.Hypotheses[0].Confidence);
        Assert.Equal("2026-09-02: fan bearing weak, temporary fix.", Assert.Single(diagnosis.Hypotheses[0].Evidence));
        Assert.Equal("replace", diagnosis.RecommendedNextAction);

        var proposal = Assert.IsType<AgentProposalDto>(item.Proposal);
        Assert.True(proposal.OutputReadable);
        Assert.Equal(WorkOrderStrategy.EscalateReplacement, proposal.Strategy);
        // Exact to the cent: read as decimal from the number's text, never via a double.
        Assert.Equal(45_000.50m, proposal.EstimatedCost);
        Assert.Equal("high", proposal.Urgency);
        Assert.StartsWith("Fourth failure", proposal.Justification);
        Assert.Empty(proposal.ConsolidateWithWorkOrderIds);
    }

    [Fact]
    public async Task Case_WhenTheDiagnosticFailed_SaysSoRatherThanReturningNothing()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 45_000m);
        var workflowId = await WorkflowIdAsync(fault.ReportId);

        await AddStepAsync(workflowId, AgentRunResponse.DiagnosticAgentName, "[]", null, "SafeFailure",
            error: "Model reply failed validation twice.");

        var diagnosis = Assert.IsType<AgentDiagnosisDto>((await CaseAsync(manager, order.Id)).Diagnosis);

        Assert.Equal("SafeFailure", diagnosis.ValidationResult);
        Assert.Equal("Model reply failed validation twice.", diagnosis.ErrorMessage);
        Assert.False(diagnosis.OutputReadable);
        Assert.Empty(diagnosis.Hypotheses);
    }

    [Fact]
    public async Task Case_WithAnUnrecognisedStrategy_LeavesItNullRatherThanGuessing()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 45_000m);

        await AddStepAsync(await WorkflowIdAsync(fault.ReportId), AgentRunResponse.StrategistAgentName, "[]",
            """{"strategy":"buy_a_new_building","estimated_cost":1,"urgency":"low","justification":"x"}""", "Ok");

        var proposal = Assert.IsType<AgentProposalDto>((await CaseAsync(manager, order.Id)).Proposal);

        Assert.Null(proposal.Strategy);
        Assert.False(proposal.OutputReadable);
    }

    // ---------------------------------------------------------------------------------
    // What the runner keeps out of a /run reply
    // ---------------------------------------------------------------------------------

    [Fact]
    public void DownstreamResults_NamesEachStepByTheFieldItArrivedIn()
    {
        var response = JsonSerializer.Deserialize<AgentRunResponse>($$"""
            {
              "workflow_id": 7, "agent": "clarifier", "status": "ok",
              "output": { "questions": [] }, "error": null, "tool_calls": [],
              "diagnosis": { "agent": "something-else", "status": "ok", "output": {{DiagnosisJson}}, "tool_calls": [] },
              "strategy": { "agent": "strategist", "status": "safe_failure", "output": null,
                            "error": "Reply was not valid JSON.", "tool_calls": [] }
            }
            """)!;

        var results = response.DownstreamResults();

        Assert.Equal(2, results.Count);

        // The field decides the name, not the envelope's own description of itself.
        Assert.Equal(AgentRunResponse.DiagnosticAgentName, results[0].AgentName);
        Assert.True(results[0].Succeeded);
        Assert.Contains("failing cooling fan", results[0].OutputJson);

        Assert.Equal(AgentRunResponse.StrategistAgentName, results[1].AgentName);
        Assert.False(results[1].Succeeded);
        Assert.Null(results[1].OutputJson);
        Assert.Equal("Reply was not valid JSON.", results[1].Error);
    }

    [Fact]
    public void DownstreamResults_FromAReplyWithoutThem_IsEmpty()
    {
        // The shape before the diagnostic existed: nothing to record, nothing thrown.
        var response = JsonSerializer.Deserialize<AgentRunResponse>("""
            { "workflow_id": 7, "agent": "clarifier", "status": "ok",
              "output": { "questions": [] }, "error": null, "tool_calls": [] }
            """)!;

        Assert.Empty(response.DownstreamResults());
    }

    // ---------------------------------------------------------------------------------
    // The dispatch board's search
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task List_Search_MatchesTheAssetTagOrTheReportDescription_IgnoringCase()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 900m);
        var other = await RaiseAsync(manager, await NewFaultAsync(), 900m);

        // The tag is random per asset, so it finds exactly this order.
        var byTag = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?search={order.AssetTag.ToLowerInvariant()}", JsonOptions);

        Assert.Equal(order.Id, Assert.Single(byTag!.Items).Id);

        // Every fault in this class has the same description, so it finds both — and the
        // search composes with an exact filter rather than replacing it.
        var byDescription = await manager.GetFromJsonAsync<PagedResult<WorkOrderDto>>(
            $"/api/workorders?search=SHUT%20ITSELF&assetId={other.AssetId}", JsonOptions);

        Assert.Equal(other.Id, Assert.Single(byDescription!.Items).Id);
    }

    // ---------------------------------------------------------------------------------
    // The technician list
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Users_ByRole_ReturnsOnlyThatRole()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician, "Zara Technician");
        var (_, reporterId) = await ClientAsync(Role.Reporter);

        var technicians = await manager.GetFromJsonAsync<List<UserDto>>("/api/users?role=Technician", JsonOptions);

        Assert.Contains(technicians!, u => u.Id == technicianId);
        Assert.DoesNotContain(technicians!, u => u.Id == reporterId);
        Assert.All(technicians!, u => Assert.Equal(Role.Technician, u.Role));
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/users?role=Wizard")]
    public async Task Users_WithoutAKnownRole_Returns400RatherThanEveryone(string path)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Users_Keeps401And403Apart()
    {
        var (technician, _) = await ClientAsync(Role.Technician);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync("/api/users?role=Technician")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await technician.GetAsync("/api/users?role=Technician")).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private const string DiagnosisJson = """
        {"hypotheses":[
            {"cause":"Overheating due to a failing cooling fan","confidence":"high",
             "evidence":["2026-09-02: fan bearing weak, temporary fix."]},
            {"cause":"Lamp near end of life","confidence":"low","evidence":["Lamp hours high."]}],
         "primary_hypothesis_index":0,
         "recommended_next_action":"replace",
         "reasoning_summary":"Cleaning is not holding."}
        """;

    private const string ProposalJson = """
        {"strategy":"escalate_replacement","estimated_cost":45000.50,"urgency":"high",
         "justification":"Fourth failure of the same thermal fault.",
         "consolidate_with_work_order_ids":[]}
        """;

    private record Fault(int ReportId, int AssetId);

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role, string fullName = "Test User")
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "ApprovalPass1", fullName, role),
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
                "/api/buildings", new CreateBuildingDto("Main Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await anonymous.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var (admin, _) = await ClientAsync(Role.Admin);

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var assetResponse = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(UniqueCode(), "Ceiling Projector", category!.Id, room!.Id, "Epson", "EB-990U",
                new DateOnly(2023, 8, 14), new DateOnly(2025, 8, 14)),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        var (reporter, _) = await ClientAsync(Role.Reporter);
        var reportResponse = await reporter.PostAsJsonAsync(
            "/api/reports", new CreateReportDto("Projector shut itself off again mid-lecture.", room.Id), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

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

    /// <summary>The whole queue, at the largest page the API allows.</summary>
    private static async Task<PagedResult<ApprovalCaseDto>> QueueAsync(HttpClient manager)
    {
        var response = await manager.GetAsync("/api/workorders/approvals?pageSize=25");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ApprovalCaseDto>>(JsonOptions))!;
    }

    /// <summary>
    /// One order's case, paging through the queue — other tests in this class leave orders in
    /// it too, so it can be past the first page.
    /// </summary>
    private static async Task<ApprovalCaseDto> CaseAsync(HttpClient manager, int orderId)
    {
        for (var page = 1; ; page++)
        {
            var result = await manager.GetFromJsonAsync<PagedResult<ApprovalCaseDto>>(
                $"/api/workorders/approvals?page={page}&pageSize=25", JsonOptions);

            var match = result!.Items.FirstOrDefault(c => c.WorkOrder.Id == orderId);
            if (match is not null)
            {
                return match;
            }

            Assert.True(page < result.TotalPages, $"Work order {orderId} is not in the approval queue.");
        }
    }

    private async Task<int> WorkflowIdAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.Id).SingleAsync();
    }

    /// <summary>A step written exactly as WorkflowRunner or InternalToolsController would.</summary>
    private async Task AddStepAsync(
        int workflowId,
        string agentName,
        string toolCallsJson,
        string? payloadJson,
        string validationResult,
        string? error = null)
    {
        using var scope = _factory.Services.CreateScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

        Assert.True(await workflows.RecordStepAsync(
            workflowId, agentName, toolCallsJson, payloadJson, 0, validationResult, error));
    }

    private async Task AddServiceRecordAsync(int assetId, DateOnly on, string note, ServiceOutcome outcome)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ServiceRecords.Add(new ServiceRecord
        {
            AssetId = assetId,
            ServicedOn = on,
            TechnicianName = "K. Perera",
            TechnicianNote = note,
            Outcome = outcome
        });

        await db.SaveChangesAsync();
    }
}
