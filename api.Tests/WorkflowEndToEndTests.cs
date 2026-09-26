using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace api.Tests;

/// <summary>
/// The agent service scripted (the C# side's STUB_MODE — no test reaches the Python service)
/// and a clock the test moves, so the verification delay passes without waiting for it.
/// </summary>
public class EndToEndApiFactory : AgentStubApiFactory
{
    /// <summary>Monday 5 October 2026, midday UTC.</summary>
    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>
/// A report's whole life, through the real endpoints, with the state asserted after every
/// step:
///
///   1. report → clarification answered → under the threshold → completed → sweep →
///      the reporter says it held → the workflow is Closed;
///   2. the same asset again → over the threshold → a manager approves → completed → sweep →
///      the reporter says it did not → the workflow is back in Diagnosing, and the runner takes
///      it through the diagnostic and the strategist to Strategizing once more.
///
/// Two things are not endpoints and are driven the way every other test drives them: the
/// runner (removed from the container in tests, so each run is a ProcessAsync call against a
/// scripted agent) and time (the clock is moved by VerificationSettings.DelayDays). The
/// scripted agent calls no tools, so every AgentStep counted here is an agent run.
///
/// A factory PER TEST, not per class: both scenarios move the clock, and a shared one would
/// make each depend on which ran first.
///
/// Each rule met on the way is pinned more narrowly elsewhere — the gate in ApprovalTests,
/// the sweep in VerificationSweepTests, the runner in WorkflowRunnerTests. What this adds is
/// that the pieces meet: every hand-off from one to the next leaves the next one able to act.
/// </summary>
public class WorkflowEndToEndTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = WorkflowStateMachineTests.JsonOptions;

    private const string Asks = """
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": [
            {"question_text": "Is the power light on?", "answer_type": "yes_no"}]}}
        """;

    private const string Diagnosis = """
        {"agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"hypotheses": [{"cause": "Loose HDMI connection", "confidence": "medium",
                    "evidence": ["Report text only"]}], "primary_hypothesis_index": 0,
                    "recommended_next_action": "repair", "reasoning_summary": "Signal drops."}}
        """;

    private const string Rediagnosis = """
        {"agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"hypotheses": [{"cause": "Failing cooling fan", "confidence": "high",
                    "evidence": ["Temporary fix on the last visit, fan rattling"]}],
                    "primary_hypothesis_index": 0,
                    "recommended_next_action": "replace", "reasoning_summary": "Came back hot."}}
        """;

    private const string Proposal = """
        {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
         "output": {"strategy": "single_job", "estimated_cost": 4500.0, "urgency": "high",
                    "justification": "One visit.", "consolidate_with_work_order_ids": []}}
        """;

    private static readonly string AsksNothing = $$"""
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Diagnosis}}, "strategy": {{Proposal}}}
        """;

    private static readonly string Resumed = $$"""
        {"workflow_id": 1, "agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Diagnosis}}, "strategy": {{Proposal}}}
        """;

    private static readonly string Rediagnosed = $$"""
        {"workflow_id": 1, "agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Rediagnosis}}, "strategy": {{Proposal}}}
        """;

    private readonly EndToEndApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Scenario1_ClarifiedUnderThresholdRepair_ThatHolds_EndsClosed()
    {
        var scene = await _factory.SceneAsync();

        var (workflowId, orderId) = await VerifiedRepairAsync(scene);

        // Three agent runs, one per agent — clarifier on the first call, diagnostic and
        // strategist on the resume — and no tool rows, because the scripted agent calls none.
        var steps = (await WorkflowAsync(scene, workflowId)).Steps;
        Assert.Equal(3, steps.Count);
        Assert.Equal(new[] { "clarifier", "diagnostic", "strategist" }, steps.Select(s => s.AgentName));
        Assert.All(steps, s => Assert.Equal("[]", s.ToolCallsJson));

        Assert.Equal(WorkOrderStatus.Completed, await OrderStatusAsync(scene, orderId));
    }

    [Fact]
    public async Task Scenario2_SameAssetOverThreshold_ThatDoesNotHold_IsDiagnosedAgain()
    {
        var scene = await _factory.SceneAsync();

        // Scenario 1 first, so this is the asset's SECOND repair.
        var (firstWorkflowId, firstOrderId) = await VerifiedRepairAsync(scene);

        // 1. A new report on the same equipment.
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowIdForAsync(scene, reportId);
        await AssertStatesAsync(scene, reportId, ReportStatus.Submitted, workflowId, WorkflowState.Submitted);

        // 2. The runner: nothing to ask, so clarifier, diagnostic and strategist in one call.
        await RunAgentAsync(workflowId, AsksNothing);
        await AssertStatesAsync(scene, reportId, ReportStatus.Submitted, workflowId, WorkflowState.Strategizing);

        // 3. Raised over the threshold on the SAME asset: it waits for a manager.
        var orderId = await RaiseAsync(scene, reportId, 42_000m, WorkOrderStatus.AwaitingApproval);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, (await WorkflowAsync(scene, workflowId)).CurrentState);
        Assert.Equal(await OrderAssetAsync(scene, firstOrderId), await OrderAssetAsync(scene, orderId));

        // 4. The manager approves.
        var approved = await scene.Manager.PostAsync($"/api/workorders/{orderId}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        Assert.Equal(WorkOrderStatus.Approved, await OrderStatusAsync(scene, orderId));
        Assert.Equal(WorkflowState.WorkOrderRaised, (await WorkflowAsync(scene, workflowId)).CurrentState);

        // 5-6. Assigned, then completed — as a temporary fix, this time.
        await AssignAsync(scene, orderId);
        await CompleteAsync(scene, orderId, ServiceOutcome.TemporaryFix,
            "cleaned vents + filter, unit still very hot after 25min, fan rattling. temporary fix.");
        Assert.Equal(WorkOrderStatus.Completed, await OrderStatusAsync(scene, orderId));
        Assert.Equal(WorkflowState.Completed, (await WorkflowAsync(scene, workflowId)).CurrentState);
        Assert.Equal(VerificationStatus.Pending, (await CheckForAsync(scene, orderId)).Status);

        // 7. The sweep, once the delay has passed.
        await SweepAfterDelayAsync(scene);
        Assert.Equal(WorkflowState.AwaitingVerification, (await WorkflowAsync(scene, workflowId)).CurrentState);
        var check = await CheckForAsync(scene, orderId);
        Assert.Equal(VerificationStatus.AwaitingReporterResponse, check.Status);

        // 8. The reporter says it is still broken: the check is Reopened and the workflow goes
        //    back to Diagnosing, remembering which repair did not hold.
        await ConfirmAsync(scene, check.Id, confirmed: false);
        Assert.Equal(VerificationStatus.Reopened, (await CheckForAsync(scene, orderId)).Status);
        var reopened = await WorkflowAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Diagnosing, reopened.CurrentState);
        Assert.Equal(orderId, reopened.ReopenedWorkOrderId);

        // 9. The runner diagnoses AGAIN — sent as a reopen, not to the clarifier — and the
        //    strategist proposes again: Strategizing once more, for a manager to act on.
        await RunAgentAsync(workflowId, Rediagnosed);
        Assert.True(_factory.Agent.Requests[^1].Reopened);
        await AssertStatesAsync(scene, reportId, ReportStatus.Submitted, workflowId, WorkflowState.Strategizing);

        // Five agent runs: the first call's three, then the re-diagnosis's two — appended, so
        // both diagnoses are there to compare.
        var detail = await WorkflowAsync(scene, workflowId);
        Assert.Equal(5, detail.Steps.Count);
        Assert.Equal(
            new[] { "clarifier", "diagnostic", "strategist", "diagnostic", "strategist" },
            detail.Steps.Select(s => s.AgentName));
        Assert.Equal(
            new[] { "Loose HDMI connection", "Failing cooling fan" },
            detail.Diagnoses.Select(d => d.Hypotheses[d.PrimaryHypothesisIndex!.Value].Cause));

        // The first repair's workflow is untouched by the second's reopen.
        var first = await WorkflowAsync(scene, firstWorkflowId);
        Assert.Equal(WorkflowState.Closed, first.CurrentState);
        Assert.Equal(3, first.Steps.Count);
    }

    [Fact]
    public async Task AnAnswer_WhenTheWorkflowIsNotAwaitingVerification_IsRecorded_AndMovesNothing()
    {
        var scene = await _factory.SceneAsync();

        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowIdForAsync(scene, reportId);
        await RunAgentAsync(workflowId, AsksNothing);

        var orderId = await RaiseAsync(scene, reportId, 500m, WorkOrderStatus.Approved);
        await AssignAsync(scene, orderId);
        await CompleteAsync(scene, orderId, ServiceOutcome.Resolved, "replaced the hdmi cable, tested 30min ok.");
        await SweepAfterDelayAsync(scene);

        // Somewhere else by the time the reporter answers — as data, since no endpoint leaves
        // it anywhere but AwaitingVerification.
        await WorkflowTestData.PutInStateAsync(_factory.Services, reportId, WorkflowState.Completed);

        var check = await CheckForAsync(scene, orderId);
        await ConfirmAsync(scene, check.Id, confirmed: false);

        // The answer is a fact about the room and stands; the workflow is not dragged along.
        Assert.Equal(VerificationStatus.Reopened, (await CheckForAsync(scene, orderId)).Status);
        var workflow = await WorkflowAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Completed, workflow.CurrentState);
        Assert.Null(workflow.ReopenedWorkOrderId);
    }

    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Scenario 1, every step asserted. Returns the workflow and the order, both finished:
    /// the workflow Closed, the order Completed with its check Confirmed.
    /// </summary>
    private async Task<(int WorkflowId, int OrderId)> VerifiedRepairAsync(StateMachineScene scene)
    {
        // 1. The report, and the workflow raised with it.
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowIdForAsync(scene, reportId);
        await AssertStatesAsync(scene, reportId, ReportStatus.Submitted, workflowId, WorkflowState.Submitted);

        // 2. The clarifier asks: human pause 1.
        await RunAgentAsync(workflowId, Asks);
        await AssertStatesAsync(scene, reportId, ReportStatus.AwaitingClarification, workflowId, WorkflowState.AwaitingClarification);

        // 3. The reporter answers.
        var questions = await scene.Reporter.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);
        var answered = await scene.Reporter.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[] { new SubmittedAnswer(Assert.Single(questions!).Id, "Yes") }),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        await AssertStatesAsync(scene, reportId, ReportStatus.Clarified, workflowId, WorkflowState.Diagnosing);

        // 4. The runner resumes at the diagnostic, then the strategist.
        await RunAgentAsync(workflowId, Resumed);
        await AssertStatesAsync(scene, reportId, ReportStatus.Clarified, workflowId, WorkflowState.Strategizing);

        // 5. Raised UNDER the threshold: approved on the spot, no manager decision.
        var orderId = await RaiseAsync(scene, reportId, 500m, WorkOrderStatus.Approved);
        Assert.Equal(WorkflowState.WorkOrderRaised, (await WorkflowAsync(scene, workflowId)).CurrentState);

        // 6. Assigned — which changes who, not the status.
        await AssignAsync(scene, orderId);
        Assert.Equal(WorkOrderStatus.Approved, await OrderStatusAsync(scene, orderId));
        Assert.Equal(WorkflowState.WorkOrderRaised, (await WorkflowAsync(scene, workflowId)).CurrentState);

        // 7. Completed by the technician, and a verification check raised for later.
        await CompleteAsync(scene, orderId, ServiceOutcome.Resolved, "replaced the hdmi cable at the lectern, tested 30min ok.");
        Assert.Equal(WorkOrderStatus.Completed, await OrderStatusAsync(scene, orderId));
        Assert.Equal(WorkflowState.Completed, (await WorkflowAsync(scene, workflowId)).CurrentState);
        Assert.Equal(VerificationStatus.Pending, (await CheckForAsync(scene, orderId)).Status);

        // 8. The sweep, once the delay has passed: the workflow and its check both fall due.
        await SweepAfterDelayAsync(scene);
        Assert.Equal(WorkflowState.AwaitingVerification, (await WorkflowAsync(scene, workflowId)).CurrentState);
        var check = await CheckForAsync(scene, orderId);
        Assert.Equal(VerificationStatus.AwaitingReporterResponse, check.Status);

        // 9. The reporter says it held: Verified, and the workflow is Closed.
        await ConfirmAsync(scene, check.Id, confirmed: true);
        Assert.Equal(VerificationStatus.Confirmed, (await CheckForAsync(scene, orderId)).Status);
        await AssertStatesAsync(scene, reportId, ReportStatus.Clarified, workflowId, WorkflowState.Closed);

        return (workflowId, orderId);
    }

    /// <summary>
    /// What the runner does when it dequeues the id. The report's own status is asserted
    /// alongside the workflow's on purpose: only clarification moves it (Submitted →
    /// AwaitingClarification → Clarified); nothing after that does, by design — see
    /// ReportStatus in CLAUDE.md.
    /// </summary>
    private async Task RunAgentAsync(int workflowId, string reply)
    {
        _factory.Agent.Replies.Enqueue(
            new AgentCallResult(true, JsonSerializer.Deserialize<AgentRunResponse>(reply)!, null, 1000));

        var runner = new WorkflowRunner(
            _factory.Services.GetRequiredService<IWorkflowQueue>(),
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowRunner>.Instance);

        await runner.ProcessAsync(workflowId, CancellationToken.None);
    }

    private async Task SweepAfterDelayAsync(StateMachineScene scene)
    {
        var delayDays = _factory.Services.GetRequiredService<VerificationSettings>().DelayDays;
        _factory.Clock.Now = _factory.Clock.Now.AddDays(delayDays);

        var response = await scene.Manager.PostAsync("/api/workflows/verification-sweep", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Exactly this repair's workflow and check: an earlier one is already answered.
        var result = await response.Content.ReadFromJsonAsync<VerificationSweepResultDto>(JsonOptions);
        Assert.Equal(1, result!.WorkflowsAwaitingVerification);
        Assert.Equal(1, result.AskedReporter);
        Assert.Equal(0, result.Failed);
    }

    private static async Task<int> RaiseAsync(StateMachineScene scene, int reportId, decimal cost, WorkOrderStatus expected)
    {
        var response = await scene.Manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(reportId, scene.AssetId, WorkOrderStrategy.SingleJob, cost, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);
        Assert.Equal(expected, order!.Status);
        return order.Id;
    }

    private static async Task AssignAsync(StateMachineScene scene, int orderId)
    {
        var response = await scene.Manager.PutAsJsonAsync(
            $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(scene.TechnicianId), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task CompleteAsync(StateMachineScene scene, int orderId, ServiceOutcome outcome, string note)
    {
        var response = await scene.Technician.PostAsJsonAsync(
            $"/api/workorders/{orderId}/complete", new CompleteWorkOrderDto(480m, outcome, note, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task ConfirmAsync(StateMachineScene scene, int checkId, bool confirmed)
    {
        var response = await scene.Reporter.PostAsJsonAsync(
            $"/api/verifications/{checkId}/confirm", new ReporterConfirmationDto(confirmed, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task AssertStatesAsync(
        StateMachineScene scene, int reportId, ReportStatus report, int workflowId, WorkflowState workflow)
    {
        var detail = await scene.Reporter.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}", JsonOptions);
        Assert.Equal(report.ToString(), detail.GetProperty("status").GetString());
        Assert.Equal(workflow, (await WorkflowAsync(scene, workflowId)).CurrentState);
    }

    private static async Task<WorkflowDetailDto> WorkflowAsync(StateMachineScene scene, int workflowId) =>
        (await scene.Manager.GetFromJsonAsync<WorkflowDetailDto>($"/api/workflows/{workflowId}", JsonOptions))!;

    private static async Task<int> WorkflowIdForAsync(StateMachineScene scene, int reportId)
    {
        var page = await scene.Manager.GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=100", JsonOptions);
        return Assert.Single(page!.Items, w => w.ReportId == reportId).Id;
    }

    private static async Task<WorkOrderStatus> OrderStatusAsync(StateMachineScene scene, int orderId)
    {
        var order = await scene.Manager.GetFromJsonAsync<JsonElement>($"/api/workorders/{orderId}", JsonOptions);
        return Enum.Parse<WorkOrderStatus>(order.GetProperty("status").GetString()!);
    }

    private static async Task<int> OrderAssetAsync(StateMachineScene scene, int orderId)
    {
        var order = await scene.Manager.GetFromJsonAsync<JsonElement>($"/api/workorders/{orderId}", JsonOptions);
        return order.GetProperty("asset").GetProperty("id").GetInt32();
    }

    /// <summary>The check raised for the order, as the reporter who is asked about it sees it.</summary>
    private static async Task<VerificationCheckDto> CheckForAsync(StateMachineScene scene, int orderId)
    {
        var page = await scene.Reporter.GetFromJsonAsync<PagedResult<VerificationCheckDto>>(
            "/api/verifications?page=1&pageSize=100", JsonOptions);
        return Assert.Single(page!.Items, c => c.WorkOrderId == orderId);
    }
}
