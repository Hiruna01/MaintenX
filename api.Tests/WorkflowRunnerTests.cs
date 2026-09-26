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

/// <summary>Stands in for the agent service: answers each /run with the next scripted reply.</summary>
public class ScriptedAgentClient : IAgentClient
{
    public Queue<AgentCallResult> Replies { get; } = new();

    public List<AgentRunRequest> Requests { get; } = new();

    public Task<AgentCallResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Replies.Dequeue());
    }
}

/// <summary>An ApiFactory whose agent service is a script — no test reaches the real one.</summary>
public class AgentStubApiFactory : StateMachineApiFactory
{
    public ScriptedAgentClient Agent { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentClient>();
            services.AddSingleton<IAgentClient>(Agent);
        });
    }
}

/// <summary>
/// WorkflowRunner, one run at a time (it is removed from the container in every test, so
/// each test calls ProcessAsync itself). What is pinned:
///
///   * it advances ONE AGENT AT A TIME — a step recorded, then that agent's transition —
///     and every step is visible through the polling read, GET /api/workflows/{id};
///   * it STOPS at human pause 1 with the clarifier's step alone, and a workflow waiting on
///     a person is never run again by a stray queue entry;
///   * the reporter's answers resume the run at the diagnostic, WITH the answers, and the
///     clarifier is not asked again;
///   * it never reaches human pause 2 itself: it waits in Strategizing, and the manager
///     raising the order is what moves it on;
///   * an agent service that is down ends the run in Failed with the reason, and a manager
///     can still raise the order by hand;
///   * a repair the reporter says did not hold runs the diagnostic again — not the clarifier — on the
///     asset the failed order names, with the ServiceRecord that repair appended visible to
///     its tool, and the second diagnosis is appended beside the first, never over it.
/// </summary>
public class WorkflowRunnerTests : IClassFixture<AgentStubApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = WorkflowStateMachineTests.JsonOptions;

    private const string Asks = """
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": [
            {"question_text": "Is the power light on?", "answer_type": "yes_no"},
            {"question_text": "How often does it cut out?", "answer_type": "single_select",
             "options": ["Once", "Every lecture"]}]}}
        """;

    private const string Diagnosis = """
        {"agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"hypotheses": [{"cause": "Overheating", "confidence": "high",
                    "evidence": ["2026-09-02: fan bearing weak"]}],
                    "recommended_next_action": "repair", "reasoning_summary": "Thermal."}}
        """;

    private const string Proposal = """
        {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
         "output": {"strategy": "single_job", "estimated_cost": 4500.0, "urgency": "high",
                    "justification": "Replace the fan.", "consolidate_with_work_order_ids": []}}
        """;

    private static readonly string AsksNothing = $$"""
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Diagnosis}}, "strategy": {{Proposal}}}
        """;

    // The second opinion, after the repair did not hold. Deliberately a different cause from
    // Diagnosis, so the test can tell which step holds which.
    private const string Rediagnosis = """
        {"agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"hypotheses": [{"cause": "Failing cooling fan", "confidence": "high",
                    "evidence": ["temporary fix after the repair, fan noisy"]}],
                    "primary_hypothesis_index": 0,
                    "recommended_next_action": "replace", "reasoning_summary": "Came back."}}
        """;

    private static readonly string Rediagnosed = $$"""
        {"workflow_id": 1, "agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Rediagnosis}}, "strategy": {{Proposal}}}
        """;

    private const string TemporaryFixNote =
        "cleaned vents + filter. still very hot to touch after 25min, fan noisy. temporary fix.";

    private static readonly string Resumed = $$"""
        {"workflow_id": 1, "agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Diagnosis}}, "strategy": {{Proposal}}}
        """;

    private readonly AgentStubApiFactory _factory;

    public WorkflowRunnerTests(AgentStubApiFactory factory)
    {
        _factory = factory;
        _factory.Agent.Replies.Clear();
        _factory.Agent.Requests.Clear();
    }

    [Fact]
    public async Task AReportTheClarifierAsksAbout_StopsAtAwaitingClarification_AndIsNotRunAgain()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        _factory.Agent.Replies.Enqueue(Reply(Asks, durationMs: 1200));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.AwaitingClarification, detail.CurrentState);

        var step = Assert.Single(detail.Steps);
        Assert.Equal(AgentRunResponse.ClarifierAgentName, step.AgentName);
        Assert.Equal("[]", step.ToolCallsJson);
        Assert.Equal(1200, step.DurationMs);
        Assert.Equal("Ok", step.ValidationResult);

        // A fresh report goes out with no answers — the field is left off the wire entirely.
        Assert.Null(Assert.Single(_factory.Agent.Requests).ClarificationAnswers);

        // A stray queue entry for a workflow waiting on its reporter does nothing at all.
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        Assert.Single(_factory.Agent.Requests);
        Assert.Equal(WorkflowState.AwaitingClarification, (await PollAsync(scene, workflowId)).CurrentState);
    }

    [Fact]
    public async Task AReportWithNothingToAsk_AdvancesOneAgentAtATime_AndWaitsInStrategizingForAManager()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowOfAsync(scene, reportId);

        _factory.Agent.Replies.Enqueue(Reply(AsksNothing, durationMs: 1500));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);
        Assert.Contains("raise the work order", detail.Outcome);

        // One step per agent, in graph order; the whole call's time on the first.
        Assert.Equal(
            new[] { ("clarifier", 1500), ("diagnostic", 0), ("strategist", 0) },
            detail.Steps.Select(s => (s.AgentName, s.DurationMs)));
        Assert.All(detail.Steps, s => Assert.Equal("Ok", s.ValidationResult));

        // Human pause 2 is reached by a manager raising the order, never by the runner: over
        // the threshold, so it waits for a decision.
        var raised = await RaiseAsync(scene, reportId, 42_000m);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, (await PollAsync(scene, workflowId)).CurrentState);
    }

    [Fact]
    public async Task AnsweredQuestions_ResumeTheRunAtTheDiagnostic_WithTheAnswers()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowOfAsync(scene, reportId);

        _factory.Agent.Replies.Enqueue(Reply(Asks));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var questions = await scene.Reporter.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);
        var answered = await scene.Reporter.PostAsJsonAsync(
            $"/api/reports/{reportId}/clarifications",
            new SubmitAnswersRequest(new[]
            {
                new SubmittedAnswer(questions![0].Id, "No"),
                new SubmittedAnswer(questions[1].Id, "Every lecture")
            }),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        Assert.Equal(WorkflowState.Diagnosing, (await PollAsync(scene, workflowId)).CurrentState);

        _factory.Agent.Replies.Enqueue(Reply(Resumed, durationMs: 900));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        // The second call carries THIS run's answers, in the order they were asked — which is
        // what sends graph.py straight to the diagnostic.
        var resume = _factory.Agent.Requests[1];
        Assert.Equal(
            new[]
            {
                new AgentClarificationAnswer("Is the power light on?", "No"),
                new AgentClarificationAnswer("How often does it cut out?", "Every lecture")
            },
            resume.ClarificationAnswers);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);

        // No second clarifier step: it did not run. The diagnostic ran first, so it has the time.
        Assert.Equal(
            new[] { ("clarifier", 1000), ("diagnostic", 900), ("strategist", 0) },
            detail.Steps.Select(s => (s.AgentName, s.DurationMs)));
    }

    [Fact]
    public async Task AnAgentServiceThatIsDown_FailsTheRunWithTheReason_AndAManagerCanStillRaiseTheOrder()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowOfAsync(scene, reportId);

        const string Reason = "Could not reach the agent service: Connection refused.";
        _factory.Agent.Replies.Enqueue(new AgentCallResult(false, null, Reason, 30));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Failed, detail.CurrentState);
        Assert.Equal(Reason, detail.Outcome);
        Assert.Equal("CallFailed", Assert.Single(detail.Steps).ValidationResult);

        // The agent failing costs advice, not the ability to act.
        var raised = await RaiseAsync(scene, reportId, 500m);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        Assert.Equal(WorkflowState.WorkOrderRaised, (await PollAsync(scene, workflowId)).CurrentState);
    }

    /// <summary>
    /// The agent's RunRequest is extra="forbid" and its clarification_answers is a list, so a
    /// JSON null there would be a 422 on every fresh report. Serialised the way AgentClient's
    /// PostAsJsonAsync does it.
    /// </summary>
    [Fact]
    public void TheAnswersAreSnakeCaseOnTheWire_AndLeftOffWhenThereAreNone()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fresh = JsonSerializer.Serialize(new AgentRunRequest(1, "Projector cutting out.", 3, null), web);
        Assert.DoesNotContain("clarification_answers", fresh);

        var resumed = JsonSerializer.Serialize(
            new AgentRunRequest(1, "Projector cutting out.", 3, null, null,
                new[] { new AgentClarificationAnswer("Is the power light on?", "No") }),
            web);
        Assert.Contains("\"clarification_answers\":[{\"question_text\":\"Is the power light on?\",\"answer_text\":\"No\"}]", resumed);
    }

    [Fact]
    public async Task AReopenedRepair_RunsTheDiagnosticAgain_OnTheOrdersAsset_AndKeepsBothDiagnoses()
    {
        var scene = await _factory.SceneAsync();
        var (reportId, workflowId, orderId) = await CompletedRepairAsync(scene);

        // Where the reporter's "no" leaves it. The sweep and the confirm that get it there
        // through the endpoints are WorkflowEndToEndTests'; this pins what the runner does next.
        await WorkflowTestData.ReopenedAsync(_factory.Services, reportId, orderId);

        var reopened = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Diagnosing, reopened.CurrentState);
        Assert.Equal(orderId, reopened.ReopenedWorkOrderId);

        _factory.Agent.Replies.Enqueue(Reply(Rediagnosed, durationMs: 1100));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        // Flagged as a reopen — which is what sends graph.py to the diagnostic, not the
        // clarifier — and carrying the ORDER's asset: the report never named one, and without
        // it the second run would have no history to read.
        var second = _factory.Agent.Requests[1];
        Assert.True(second.Reopened);
        Assert.Equal(scene.AssetId, second.AssetId);
        Assert.Null(second.ClarificationAnswers);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);

        // Appended, not replaced: the first run's three steps untouched, then the second run's
        // two — the diagnostic ran first in that call, so it carries the call's time.
        var agentRuns = detail.Steps.Where(s => s.ToolCallsJson == "[]").ToList();
        Assert.Equal(
            new[] { ("clarifier", 1500), ("diagnostic", 0), ("strategist", 0), ("diagnostic", 1100), ("strategist", 0) },
            agentRuns.Select(s => (s.AgentName, s.DurationMs)));

        var diagnoses = agentRuns.Where(s => s.AgentName == AgentRunResponse.DiagnosticAgentName).ToList();
        Assert.Contains("Overheating", diagnoses[0].PayloadJson);
        Assert.Contains("Failing cooling fan", diagnoses[1].PayloadJson);
        Assert.DoesNotContain("Failing cooling fan", diagnoses[0].PayloadJson);

        // And read back, one entry per run, for the workflow view to set side by side — by the
        // approval queue's own reader, so the second is the one it now shows too.
        Assert.Equal(diagnoses.Select(d => d.Id), detail.Diagnoses.Select(d => d.StepId));
        var rediagnosis = detail.Diagnoses[1];
        Assert.True(rediagnosis.OutputReadable);
        Assert.Equal("Failing cooling fan", rediagnosis.Hypotheses[rediagnosis.PrimaryHypothesisIndex!.Value].Cause);
        Assert.Equal("replace", rediagnosis.RecommendedNextAction);

        // What the second run's history tool reads: the record the completion appended, newest
        // first. Nothing is cached between runs — the tool reads the table as it is now.
        var history = await CallToolAsync("get_asset_service_history", workflowId, second.AssetId!.Value);
        var newest = history.GetProperty("result")[0];
        Assert.Equal(orderId, newest.GetProperty("workOrderId").GetInt32());
        Assert.Equal("TemporaryFix", newest.GetProperty("outcome").GetString());
        Assert.Equal(TemporaryFixNote, newest.GetProperty("technicianNote").GetString());
    }

    [Fact]
    public void TheReopenFlagIsSnakeCaseOnTheWire_AndLeftOffWhenFalse()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fresh = JsonSerializer.Serialize(new AgentRunRequest(1, "Projector cutting out.", 3, null), web);
        Assert.DoesNotContain("reopened", fresh);

        var reopened = JsonSerializer.Serialize(
            new AgentRunRequest(1, "Projector cutting out.", 3, null, 7, Reopened: true), web);
        Assert.Contains("\"reopened\":true", reopened);
        Assert.Contains("\"asset_id\":7", reopened);
    }

    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A report run to Strategizing by the runner, its order raised, assigned and completed
    /// through the real endpoints as a temporary fix — so the ServiceRecord is the one
    /// completion appends, not one written for the test. The workflow ends Completed.
    /// </summary>
    private async Task<(int ReportId, int WorkflowId, int OrderId)> CompletedRepairAsync(StateMachineScene scene)
    {
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowOfAsync(scene, reportId);

        _factory.Agent.Replies.Enqueue(Reply(AsksNothing, durationMs: 1500));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var raised = await RaiseAsync(scene, reportId, 500m);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var orderId = (await raised.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions))!.Id;

        var assigned = await scene.Manager.PutAsJsonAsync(
            $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(scene.TechnicianId), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        var completed = await scene.Technician.PostAsJsonAsync(
            $"/api/workorders/{orderId}/complete",
            new CompleteWorkOrderDto(480m, ServiceOutcome.TemporaryFix, TemporaryFixNote, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.Equal(WorkflowState.Completed, (await PollAsync(scene, workflowId)).CurrentState);

        return (reportId, workflowId, orderId);
    }

    /// <summary>A tool call exactly as the agent service makes it: the shared secret, no JWT.</summary>
    private async Task<JsonElement> CallToolAsync(string toolName, int workflowId, int id)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", ApiFactory.AgentSharedSecret);

        var response = await client.PostAsJsonAsync(
            $"/api/internal/tools/{toolName}", new ToolCallRequest(workflowId, id, "diagnostic"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private WorkflowRunner Runner() => new(
        _factory.Services.GetRequiredService<IWorkflowQueue>(),
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<WorkflowRunner>.Instance);

    private static AgentCallResult Reply(string json, int durationMs = 1000) =>
        new(true, JsonSerializer.Deserialize<AgentRunResponse>(json)!, null, durationMs);

    private async Task<int> WorkflowOfNewReportAsync(StateMachineScene scene) =>
        await WorkflowOfAsync(scene, await scene.FileReportAsync());

    private static async Task<int> WorkflowOfAsync(StateMachineScene scene, int reportId)
    {
        var page = await scene.Manager.GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=100", JsonOptions);

        return Assert.Single(page!.Items, w => w.ReportId == reportId).Id;
    }

    /// <summary>What a client polling the workflow sees.</summary>
    private static async Task<WorkflowDetailDto> PollAsync(StateMachineScene scene, int workflowId) =>
        (await scene.Manager.GetFromJsonAsync<WorkflowDetailDto>($"/api/workflows/{workflowId}", JsonOptions))!;

    private static Task<HttpResponseMessage> RaiseAsync(StateMachineScene scene, int reportId, decimal cost) =>
        scene.Manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(reportId, scene.AssetId, WorkOrderStrategy.SingleJob, cost, null),
            JsonOptions);
}
