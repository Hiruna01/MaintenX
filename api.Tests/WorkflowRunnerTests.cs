using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>Runs while the "agent" is working, before it replies — what happens mid-call.</summary>
    public Func<AgentRunRequest, Task>? DuringCall { get; set; }

    public async Task<AgentCallResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (DuringCall is not null)
        {
            await DuringCall(request);
        }

        return Replies.Dequeue();
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
///   * it RAISES the strategist's proposal through the approval gate when the report names
///     its asset — the gate, not the runner, decides whether that is human pause 2 — and
///     otherwise waits in Strategizing for a manager to raise one;
///   * a revision runs the strategist ALONE with the manager's note and resubmits the SAME
///     order through the same gate; a revision it cannot act on waits for the manager, and is
///     never run twice;
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

        // The second proposal names the reopened order's asset, so the runner raises it
        // through the approval gate — a NEW order, within the threshold, approved at once.
        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.WorkOrderRaised, detail.CurrentState);

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

        // The plan gains the re-diagnosis as two NEW steps, appended and settled, beside the
        // first run's three — delegated in the plan as well as recorded in the steps.
        Assert.Equal(
            new[]
            {
                ("clarifier", "completed", "api"), ("diagnostic", "completed", "api"), ("strategist", "completed", "api"),
                ("diagnostic", "completed", "api"), ("strategist", "completed", "api")
            },
            detail.Plan!.Steps.Select(p => (p.Agent, p.Status, p.AddedBy)));
        Assert.Contains($"#{orderId}", detail.Plan.Steps[3].Purpose);

        // What the second run's history tool reads: the record the completion appended, newest
        // first. Nothing is cached between runs — the tool reads the table as it is now.
        var history = await CallToolAsync("get_asset_service_history", workflowId, second.AssetId!.Value);
        var newest = history.GetProperty("result")[0];
        Assert.Equal(orderId, newest.GetProperty("workOrderId").GetInt32());
        Assert.Equal("TemporaryFix", newest.GetProperty("outcome").GetString());
        Assert.Equal(TemporaryFixNote, newest.GetProperty("technicianNote").GetString());
    }

    // ---------------------------------------------------------------------------------
    // The runner raises the proposal — through the gate — and a revision closes the loop.
    // ---------------------------------------------------------------------------------

    private const string ReplacementProposal = """
        {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
         "output": {"strategy": "escalate_replacement", "estimated_cost": 45000.00, "urgency": "high",
                    "justification": "Third thermal failure.", "consolidate_with_work_order_ids": []}}
        """;

    private const string RevisionNote = "Too dear this term - price a fan and filter swap first.";

    private static string AsksNothingProposing(string proposal) => $$"""
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "diagnosis": {{Diagnosis}}, "strategy": {{proposal}}}
        """;

    // A revision run: the strategist alone, so its fields are the top-level ones too.
    private const string RevisedProposal = """
        {"workflow_id": 1, "agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
         "attempts": 1, "duration_ms": 600, "output": {"questions": []},
         "strategy": {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
                      "attempts": 1, "duration_ms": 600,
                      "output": {"strategy": "known_fix", "estimated_cost": 6500.00, "urgency": "medium",
                                 "justification": "Fan and filter swap, as the manager asked.",
                                 "consolidate_with_work_order_ids": []}}}
        """;

    /// <summary>
    /// The report names its equipment (a sticker scan), so the proposal can be raised — and
    /// the runner raises it through CreateAsync: over the threshold and a replacement, so the
    /// GATE sends it to a manager. The gate's step says the runner raised it. Verified to fail
    /// with the raise removed from AdvanceThroughDownstreamAsync.
    /// </summary>
    [Fact]
    public async Task AProposalForANamedAsset_IsRaisedByTheRunner_ThroughTheApprovalGate()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await FileReportOnTheAssetAsync(scene);
        var workflowId = await WorkflowOfAsync(scene, reportId);

        _factory.Agent.Replies.Enqueue(Reply(AsksNothingProposing(ReplacementProposal)));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, detail.CurrentState);

        var order = Assert.Single(await OrdersForReportAsync(reportId));
        Assert.Equal(
            (WorkOrderStatus.AwaitingApproval, WorkOrderStrategy.EscalateReplacement, 45_000m, scene.AssetId),
            (order.Status, order.Strategy, order.EstimatedCost, order.AssetId));

        var gate = detail.Steps[^1];
        Assert.Equal(("approval", "ApprovalRequired"), (gate.AgentName, gate.ValidationResult));
        Assert.Contains("Raised by the workflow runner", gate.PayloadJson);
        Assert.Contains($"Work order {order.Id}", detail.Outcome);
    }

    /// <summary>
    /// A proposal the API cannot raise as it stands — a strategy it does not know, or an
    /// estimate finer than the numeric(18,2) column — is not guessed at or rounded: nothing is
    /// raised, and the workflow waits in Strategizing for a manager.
    /// </summary>
    [Theory]
    [InlineData("\"rebuild\"", "4500.00")]
    [InlineData("\"single_job\"", "4500.555")]
    public async Task AProposalTheApiCannotRaise_IsLeftForAManager(string strategy, string cost)
    {
        var scene = await _factory.SceneAsync();
        var reportId = await FileReportOnTheAssetAsync(scene);
        var workflowId = await WorkflowOfAsync(scene, reportId);

        var proposal = $$$"""
            {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [],
             "output": {"strategy": {{{strategy}}}, "estimated_cost": {{{cost}}}, "urgency": "high",
                        "justification": "x", "consolidate_with_work_order_ids": []}}
            """;
        _factory.Agent.Replies.Enqueue(Reply(AsksNothingProposing(proposal)));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.Strategizing, (await PollAsync(scene, workflowId)).CurrentState);
        Assert.Empty(await OrdersForReportAsync(reportId));
    }

    /// <summary>
    /// THE REVISION LOOP, CLOSED. A manager sends the runner's order back with a note; the
    /// runner sends the note and the order's id to the strategist alone, on the order's asset;
    /// the revised proposal is recorded beside the first, planned as an appended step, and the
    /// SAME order is resubmitted through the gate — within the threshold now, so approved with
    /// nobody deciding. One order on the report, start to finish. Verified to fail with the
    /// Strategizing branch removed from ProcessAsync.
    /// </summary>
    [Fact]
    public async Task ARevision_RunsTheStrategistAlone_WithTheNote_AndResubmitsTheSameOrder()
    {
        var scene = await _factory.SceneAsync();
        var (reportId, workflowId, orderId) = await RevisionRequestedAsync(scene);

        _factory.Agent.Replies.Enqueue(Reply(RevisedProposal, durationMs: 700));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var request = _factory.Agent.Requests[^1];
        Assert.Equal(RevisionNote, request.RevisionNote);
        Assert.Equal(orderId, request.RevisionWorkOrderId);
        Assert.Equal(scene.AssetId, request.AssetId);
        Assert.Null(request.ClarificationAnswers);
        Assert.False(request.Reopened);

        var order = Assert.Single(await OrdersForReportAsync(reportId));
        Assert.Equal(
            (orderId, WorkOrderStatus.Approved, WorkOrderStrategy.KnownFix, 6_500m, RevisionNote),
            (order.Id, order.Status, order.Strategy, order.EstimatedCost, order.RevisionNote));

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.WorkOrderRaised, detail.CurrentState);
        Assert.Equal(
            new[]
            {
                ("clarifier", "Ok"), ("diagnostic", "Ok"), ("strategist", "Ok"),
                ("approval", "ApprovalRequired"), ("approval", "RevisionRequested"),
                ("strategist", "Ok"), ("approval", "AutoApproved")
            },
            detail.Steps.Select(st => (st.AgentName, st.ValidationResult)));
        Assert.Equal(600, detail.Steps[5].DurationMs);
        Assert.Contains("Resubmitted by the workflow runner", detail.Steps[^1].PayloadJson);

        // The revised proposal is delegated in the plan too — appended, the first one kept.
        var planned = detail.Plan!.Steps;
        Assert.Equal(
            new[] { ("strategist", "completed"), ("strategist", "completed") },
            planned.Where(p => p.Agent == "strategist").Select(p => (p.Agent, p.Status)));
        Assert.Contains($"#{orderId}", planned[^1].Purpose);

        // Answered: a stray queue entry runs nothing more.
        var calls = _factory.Agent.Requests.Count;
        await Runner().ProcessAsync(workflowId, CancellationToken.None);
        Assert.Equal(calls, _factory.Agent.Requests.Count);
    }

    /// <summary>
    /// The strategist cannot be re-run — the agent service is down. The workflow is NOT failed:
    /// it stays in Strategizing with the Draft, the failed call is on a strategist step (which
    /// marks the revision answered, so it is not run again), and a manager resubmits the same
    /// order through the same gate.
    /// </summary>
    [Fact]
    public async Task ARevisionTheRunnerCannotAnswer_WaitsForAManagerToResubmit_AndIsNotRunAgain()
    {
        var scene = await _factory.SceneAsync();
        var (reportId, workflowId, orderId) = await RevisionRequestedAsync(scene);

        const string Reason = "Could not reach the agent service: Connection refused.";
        _factory.Agent.Replies.Enqueue(new AgentCallResult(false, null, Reason, 30));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);
        Assert.Contains($"resubmit work order {orderId}", detail.Outcome);
        Assert.Equal(("strategist", "CallFailed", Reason), (detail.Steps[^1].AgentName, detail.Steps[^1].ValidationResult, detail.Steps[^1].ErrorMessage));
        Assert.Equal(PlanStepStatus.Failed, detail.Plan!.Steps[^1].Status);
        Assert.Equal(WorkOrderStatus.Draft, Assert.Single(await OrdersForReportAsync(reportId)).Status);

        var calls = _factory.Agent.Requests.Count;
        await Runner().ProcessAsync(workflowId, CancellationToken.None);
        Assert.Equal(calls, _factory.Agent.Requests.Count);

        var resubmitted = await scene.Manager.PostAsJsonAsync($"/api/workorders/{orderId}/resubmit",
            new ResubmitWorkOrderDto(WorkOrderStrategy.SingleJob, 20_000m, "Fan, filter."), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, resubmitted.StatusCode);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, Assert.Single(await OrdersForReportAsync(reportId)).Status);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, (await PollAsync(scene, workflowId)).CurrentState);
    }

    /// <summary>
    /// The queue is in memory. A revision the strategist has not answered is unfinished work,
    /// so a restart queues it again; one it has answered (even by failing) is not, and nor is
    /// a workflow merely waiting in Strategizing for a manager. Verified to fail with the
    /// pending revisions left out of RequeueUnfinishedRunsAsync.
    /// </summary>
    [Fact]
    public async Task AtStartup_ARevisionNotYetAnswered_IsQueuedAgain_AndNoOtherStrategizingRun()
    {
        var scene = await _factory.SceneAsync();
        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();

        var (_, pending, _) = await RevisionRequestedAsync(scene);

        var (_, answered, _) = await RevisionRequestedAsync(scene);
        _factory.Agent.Replies.Enqueue(new AgentCallResult(false, null, "down", 30));
        await Runner().ProcessAsync(answered, CancellationToken.None);

        // No asset named, so nothing was raised: waiting on a manager, not on the runner.
        var waiting = await WorkflowOfNewReportAsync(scene);
        _factory.Agent.Replies.Enqueue(Reply(AsksNothing));
        await Runner().ProcessAsync(waiting, CancellationToken.None);
        Assert.Equal(WorkflowState.Strategizing, (await PollAsync(scene, waiting)).CurrentState);

        await DrainAsync(queue);
        await Runner().RequeueUnfinishedRunsAsync(CancellationToken.None);

        var requeued = await DrainAsync(queue);
        Assert.Contains(pending, requeued);
        Assert.DoesNotContain(answered, requeued);
        Assert.DoesNotContain(waiting, requeued);
    }

    /// <summary>
    /// A report naming the scene's asset, run to an order the runner raised over the threshold,
    /// then sent back by a manager: Draft, Strategizing, and the workflow re-queued.
    /// </summary>
    private async Task<(int ReportId, int WorkflowId, int OrderId)> RevisionRequestedAsync(StateMachineScene scene)
    {
        var reportId = await FileReportOnTheAssetAsync(scene);
        var workflowId = await WorkflowOfAsync(scene, reportId);

        _factory.Agent.Replies.Enqueue(Reply(AsksNothingProposing(ReplacementProposal)));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);
        var orderId = Assert.Single(await OrdersForReportAsync(reportId)).Id;

        var revised = await scene.Manager.PostAsJsonAsync(
            $"/api/workorders/{orderId}/request-revision", new RequestRevisionDto(RevisionNote), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, revised.StatusCode);
        Assert.Equal(WorkflowState.Strategizing, (await PollAsync(scene, workflowId)).CurrentState);

        return (reportId, workflowId, orderId);
    }

    private static async Task<int> FileReportOnTheAssetAsync(StateMachineScene scene)
    {
        var response = await scene.Reporter.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Projector keeps cutting out mid-lecture.", scene.RoomId, scene.AssetId),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions))!.Id;
    }

    private async Task<List<WorkOrder>> OrdersForReportAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .WorkOrders.AsNoTracking().Where(w => w.ReportId == reportId).OrderBy(w => w.Id).ToListAsync();
    }

    // ---------------------------------------------------------------------------------
    // The plan. The planner's reply is recorded verbatim on its own step; what is STORED as
    // the workflow's plan is the one PlanRules has checked, or the fallback with the reason.
    // Each plan step is then settled as its agent's result is recorded.
    // ---------------------------------------------------------------------------------

    private static string PlanEnvelope(string steps, string status = "ok", int attempts = 1, int durationMs = 300) =>
        status == "ok"
            ? $$"""
                {"agent": "planner", "status": "ok", "error": null, "tool_calls": [],
                 "attempts": {{attempts}}, "duration_ms": {{durationMs}},
                 "output": {"steps": {{steps}}, "rationale": "The report does not say whether it is dead or intermittent."} }
                """
            : $$"""
                {"agent": "planner", "status": "safe_failure", "error": "provider timed out", "tool_calls": [],
                 "attempts": {{attempts}}, "duration_ms": {{durationMs}}, "output": null}
                """;

    private const string FullPlanSteps = """
        [{"agent": "clarifier", "purpose": "Find out whether it is dead or cuts out."},
         {"agent": "diagnostic", "purpose": "Propose the cause from its history."},
         {"agent": "strategist", "purpose": "Propose a resolution and cost."}]
        """;

    private const string NoClarifierSteps = """
        [{"agent": "diagnostic", "purpose": "Propose the cause from its history."},
         {"agent": "strategist", "purpose": "Propose a resolution and cost."}]
        """;

    // Each agent reporting its own attempts and time, as the agent service now does.
    private const string TimedDiagnosis = """
        {"agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [], "attempts": 2, "duration_ms": 700,
         "output": {"hypotheses": [{"cause": "Overheating", "confidence": "high",
                    "evidence": ["2026-09-02: fan bearing weak"]}],
                    "recommended_next_action": "repair", "reasoning_summary": "Thermal."}}
        """;

    private const string TimedProposal = """
        {"agent": "strategist", "status": "ok", "error": null, "tool_calls": [], "attempts": 1, "duration_ms": 500,
         "output": {"strategy": "single_job", "estimated_cost": 4500.0, "urgency": "high",
                    "justification": "Replace the fan.", "consolidate_with_work_order_ids": []}}
        """;

    private static string PlannedRun(string plan) => $$"""
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "attempts": 1, "duration_ms": 400, "output": {"questions": []},
         "plan": {{plan}}, "diagnosis": {{TimedDiagnosis}}, "strategy": {{TimedProposal}}}
        """;

    [Fact]
    public async Task AFreshRun_IsPlannedFirst_TheCheckedPlanIsStored_AndEachStepIsSettledAsItsAgentFinishes()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        _factory.Agent.Replies.Enqueue(Reply(PlannedRun(PlanEnvelope(FullPlanSteps)), durationMs: 2500));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);

        // The planner's step comes first; every agent carries ITS OWN time and attempts, not
        // a share of the call's 2500 ms. The diagnostic needed the one retry.
        Assert.Equal(
            new[] { ("planner", 300, (int?)1), ("clarifier", 400, 1), ("diagnostic", 700, 2), ("strategist", 500, 1) },
            detail.Steps.Select(s => (s.AgentName, s.DurationMs, s.Attempts)));
        Assert.All(detail.Steps, s => Assert.Equal("Ok", s.ValidationResult));

        // The plan is the planner's, stored — PlanJson is populated — and fully settled.
        Assert.NotNull(detail.PlanJson);
        var plan = detail.Plan!;
        Assert.Equal(PlanRules.SourcePlanner, plan.Source);
        Assert.Null(plan.Note);
        Assert.Equal(
            new[] { ("clarifier", "completed"), ("diagnostic", "completed"), ("strategist", "completed") },
            plan.Steps.Select(p => (p.Agent, p.Status)));
        Assert.Equal("Find out whether it is dead or cuts out.", plan.Steps[0].Purpose);
    }

    /// <summary>An agent envelope with a "usage" block added — its last closing brace replaced.</summary>
    private static string WithUsage(string envelope, string usageJson)
    {
        var trimmed = envelope.TrimEnd();
        return $"{trimmed[..^1]}, \"usage\": {usageJson}}}";
    }

    [Fact]
    public async Task EachAgentsReportedTokens_AreStoredOnItsOwnStep_AndUsageNotReportedIsNull_NotZero()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        // The planner, the clarifier (top level) and the diagnostic report usage; the
        // strategist sends a malformed block, which is "not reported", never zero and never a
        // failed run.
        var plan = WithUsage(PlanEnvelope(FullPlanSteps), """{"prompt_tokens": 410, "completion_tokens": 62}""");
        var diagnosis = WithUsage(TimedDiagnosis, """{"prompt_tokens": 1830, "completion_tokens": 240}""");
        var proposal = WithUsage(TimedProposal, """{"prompt_tokens": "lots", "completion_tokens": 90}""");
        var reply = $$"""
            {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
             "attempts": 1, "duration_ms": 400, "output": {"questions": []},
             "usage": {"prompt_tokens": 620, "completion_tokens": 35},
             "plan": {{plan}}, "diagnosis": {{diagnosis}}, "strategy": {{proposal}}}
            """;

        _factory.Agent.Replies.Enqueue(Reply(reply));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);

        Assert.Equal(
            new[]
            {
                ("planner", (int?)410, (int?)62),
                ("clarifier", 620, 35),
                ("diagnostic", 1830, 240),
                ("strategist", null, null)
            },
            detail.Steps.Select(s => (s.AgentName, s.PromptTokens, s.CompletionTokens)));
        Assert.All(detail.Steps, s => Assert.Equal("Ok", s.ValidationResult));
    }

    [Fact]
    public async Task APlanWithoutTheClarifier_GoesStraightToDiagnosis_ThroughItsOwnTrigger_AndAsksNobodyAnything()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();
        var workflowId = await WorkflowOfAsync(scene, reportId);

        // The agent service followed the plan: no clarifier ran, so the top-level fields are
        // the diagnostic's, exactly as on a resumed run.
        var skipped = $$"""
            {"workflow_id": 1, "agent": "diagnostic", "status": "ok", "error": null, "tool_calls": [],
             "attempts": 2, "duration_ms": 700, "output": {"questions": []},
             "plan": {{PlanEnvelope(NoClarifierSteps)}}, "diagnosis": {{TimedDiagnosis}}, "strategy": {{TimedProposal}}}
            """;
        _factory.Agent.Replies.Enqueue(Reply(skipped));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);

        // No clarifier step, because the clarifier never ran — not one that "asked nothing".
        Assert.Equal(new[] { "planner", "diagnostic", "strategist" }, detail.Steps.Select(s => s.AgentName));
        Assert.Equal(new[] { "diagnostic", "strategist" }, detail.Plan!.Steps.Select(p => p.Agent));
        Assert.All(detail.Plan.Steps, p => Assert.Equal(PlanStepStatus.Completed, p.Status));

        // Nobody was asked anything, and the report never waited on its reporter.
        var questions = await scene.Reporter.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);
        Assert.Empty(questions!);
    }

    [Fact]
    public async Task APlanThatFailsTheCSharpCheck_IsNotStored_TheFallbackIs_AndThePlannersStepSaysRejected()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        // Strategist before diagnostic: the agent's schema would refuse this, which is exactly
        // why the API checking it again is worth pinning — a drifted contract must not be run.
        const string outOfOrder = """
            [{"agent": "strategist", "purpose": "Replace it."},
             {"agent": "diagnostic", "purpose": "Diagnose it."}]
            """;
        _factory.Agent.Replies.Enqueue(Reply(PlannedRun(PlanEnvelope(outOfOrder))));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);

        var plannerStep = detail.Steps.First();
        Assert.Equal("planner", plannerStep.AgentName);
        Assert.Equal("Rejected", plannerStep.ValidationResult);
        Assert.Contains("out of order", plannerStep.ErrorMessage);
        // The model's plan is still on its step, verbatim — the audit copy.
        Assert.Contains("Replace it.", plannerStep.PayloadJson);

        var plan = detail.Plan!;
        Assert.Equal(PlanRules.SourceFallback, plan.Source);
        Assert.Contains("rejected", plan.Note);
        Assert.Equal(new[] { "clarifier", "diagnostic", "strategist" }, plan.Steps.Select(p => p.Agent));
        Assert.DoesNotContain("Replace it.", detail.PlanJson);
    }

    [Fact]
    public async Task APlannerThatSafeFailed_LeavesTheFallbackPlan_AndTheRunCarriesOn()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        _factory.Agent.Replies.Enqueue(Reply(PlannedRun(PlanEnvelope("[]", status: "safe_failure", attempts: 2))));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.Equal(WorkflowState.Strategizing, detail.CurrentState);

        var plannerStep = detail.Steps.First();
        Assert.Equal(("planner", "SafeFailure", (int?)2), (plannerStep.AgentName, plannerStep.ValidationResult, plannerStep.Attempts));
        Assert.Equal(PlanRules.SourceFallback, detail.Plan!.Source);
        Assert.Contains("provider timed out", detail.Plan.Note);
    }

    [Fact]
    public async Task AReplyWithNoPlan_StillLeavesAPlan_TheFallback_WithNoPlannerStep()
    {
        var scene = await _factory.SceneAsync();
        var workflowId = await WorkflowOfNewReportAsync(scene);

        _factory.Agent.Replies.Enqueue(Reply(AsksNothing));
        await Runner().ProcessAsync(workflowId, CancellationToken.None);

        var detail = await PollAsync(scene, workflowId);
        Assert.DoesNotContain(detail.Steps, s => s.AgentName == "planner");
        Assert.Equal(PlanRules.SourceFallback, detail.Plan!.Source);
        Assert.All(detail.Plan.Steps, p => Assert.Equal(PlanStepStatus.Completed, p.Status));
    }

    [Fact]
    public async Task AtStartup_EveryRunLeftSubmittedOrDiagnosing_IsQueuedAgain_AndNothingElse()
    {
        var scene = await _factory.SceneAsync();
        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();

        // One run left where a restart would strand it, and one already waiting on a manager.
        var stranded = await WorkflowOfNewReportAsync(scene);
        var waiting = await WorkflowOfNewReportAsync(scene);
        _factory.Agent.Replies.Enqueue(Reply(AsksNothing));
        await Runner().ProcessAsync(waiting, CancellationToken.None);

        // What a restart does to the in-memory queue.
        await DrainAsync(queue);

        await Runner().RequeueUnfinishedRunsAsync(CancellationToken.None);

        var requeued = await DrainAsync(queue);
        Assert.Contains(stranded, requeued);
        Assert.DoesNotContain(waiting, requeued);
    }

    /// <summary>Empties the queue and returns what was in it. DequeueAsync blocks, so a short timeout is the end.</summary>
    private static async Task<List<int>> DrainAsync(IWorkflowQueue queue)
    {
        var ids = new List<int>();

        while (true)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            try
            {
                ids.Add(await queue.DequeueAsync(timeout.Token));
            }
            catch (OperationCanceledException)
            {
                return ids;
            }
        }
    }

    [Fact]
    public void TheReopenFlagIsSnakeCaseOnTheWire_AndLeftOffWhenFalse()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fresh = JsonSerializer.Serialize(new AgentRunRequest(1, "Projector cutting out.", 3, null), web);
        Assert.DoesNotContain("reopened", fresh);
        Assert.DoesNotContain("revision", fresh);

        var revision = JsonSerializer.Serialize(
            new AgentRunRequest(1, "Projector cutting out.", 3, null, 7, RevisionNote: "Price a repair.", RevisionWorkOrderId: 57), web);
        Assert.Contains("\"revision_note\":\"Price a repair.\"", revision);
        Assert.Contains("\"revision_work_order_id\":57", revision);

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
