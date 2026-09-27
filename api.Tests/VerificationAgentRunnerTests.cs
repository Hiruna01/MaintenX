using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace api.Tests;

/// <summary>
/// An ApiFactory whose agent service is a script (the same ScriptedAgentClient as
/// AgentStubApiFactory) and whose clock the test moves — "judged since queued" is a rule about
/// two instants, so the test has to be able to put them where it wants.
/// </summary>
public class VerificationAgentApiFactory : SweepClockApiFactory
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
/// The VerificationAgent's C# side — VerificationAgentService, the pass VerificationAgentRunner
/// runs (the runner itself is removed from the container, so each test calls the pass). Each
/// test takes its OWN factory: a pass acts on every waiting check in the table. What is pinned:
///
///   * a queued check is judged on its report's LATEST workflow, with the verification request
///     on the wire, and its verdict, reason and evidence written — never its Status;
///   * the step: "verification", "[]" tool calls, output verbatim, the agent's own time and attempts;
///   * only checks waiting on the agent are sent, oldest queued first;
///   * a call that failed is retried up to MaxAttempts and then given up on; a safe failure is
///     final at once; a report with no workflow is given up on without a call;
///   * a LATE ANSWER is judged again, with the answer — including one that arrives while the
///     agent is working on the silence;
///   * the reporter's answer and the sweep wake the runner.
/// </summary>
public class VerificationAgentRunnerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Escalate = """
        {"workflow_id": 1, "agent": "verification", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}, "attempts": 2, "duration_ms": 1234,
         "verification": {"agent": "verification", "status": "ok", "error": null, "tool_calls": [],
            "attempts": 2, "duration_ms": 1234,
            "output": {"outcome": "escalate", "confidence": "high",
                       "reason": "Fourth visit for the same thermal fault.",
                       "evidence": ["2026-09-02: temporary fix", "Reporter: not fixed"]}}}
        """;

    private static string Verdict(string outcome) => Escalate.Replace("\"escalate\"", $"\"{outcome}\"");

    private const string SafeFailure = """
        {"workflow_id": 1, "agent": "verification", "status": "safe_failure", "error": "The repair could not be looked up.",
         "tool_calls": [], "output": {"questions": []}, "attempts": 0, "duration_ms": 40,
         "verification": {"agent": "verification", "status": "safe_failure", "output": null,
            "error": "The repair could not be looked up.", "tool_calls": [], "attempts": 0, "duration_ms": 40}}
        """;

    // A 200 from an agent service that ran something else: no verification envelope at all.
    private const string NoEnvelope = """
        {"workflow_id": 1, "agent": "clarifier", "status": "ok", "error": null, "tool_calls": [],
         "output": {"questions": []}}
        """;

    [Fact]
    public async Task AQueuedCheck_IsJudgedOnItsReportsLatestWorkflow_AndItsStatusIsLeftAlone()
    {
        using var factory = new VerificationAgentApiFactory();
        var now = factory.Clock.Now.UtcDateTime;

        int checkId, olderWorkflow, latestWorkflow;
        string description;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, "VJ1", now.AddDays(-8));
            description = order.Report!.Description;

            // Two runs on the report: a failed first one, then the one the repair came out of,
            // Closed by the reporter's yes. The LATEST is the run's home.
            olderWorkflow = await AddWorkflowAsync(db, order.ReportId, WorkflowState.Failed);
            latestWorkflow = await AddWorkflowAsync(db, order.ReportId, WorkflowState.Closed);

            checkId = await AddCheckAsync(db, order, VerificationStatus.Confirmed, confirmed: true,
                comment: "  Works now.  ", queuedAt: now.AddMinutes(-5));
        }

        factory.Agent.Replies.Enqueue(Reply(Escalate));

        var pass = await PassAsync(factory);
        Assert.Equal(new VerificationAgentPassResult(Judged: 1, Retrying: 0, GaveUp: 0, Failed: 0), pass);

        // THE REQUEST: the latest workflow, the original report's words, and the verification
        // block — which routes graph.py to the verifier and nothing else.
        var request = Assert.Single(factory.Agent.Requests);
        Assert.Equal(latestWorkflow, request.WorkflowId);
        Assert.Equal(description, request.Description);
        Assert.NotNull(request.Verification);
        Assert.True(request.Verification!.ReporterConfirmed);
        Assert.Equal("Works now.", request.Verification.ReporterComment);

        // On the wire, snake_case, as the agent's extra="forbid" models read it.
        using (var wire = JsonDocument.Parse(JsonSerializer.Serialize(request)))
        {
            var block = wire.RootElement.GetProperty("verification");
            Assert.True(block.GetProperty("work_order_id").GetInt32() > 0);
            Assert.True(block.GetProperty("reporter_confirmed").GetBoolean());
            Assert.False(wire.RootElement.TryGetProperty("clarification_answers", out _));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var check = await db.VerificationChecks.AsNoTracking().SingleAsync(v => v.Id == checkId);

            // The agent's opinion, beside the reporter's answer. The STATUS is still what the
            // reporter's yes made it: nothing acts on the agent's "escalate".
            Assert.Equal("escalate", check.AgentOutcome);
            Assert.Equal("Fourth visit for the same thermal fault.", check.AgentReason);
            Assert.Equal(new[] { "2026-09-02: temporary fix", "Reporter: not fixed" },
                JsonSerializer.Deserialize<string[]>(check.AgentEvidenceJson!));
            Assert.Equal(VerificationStatus.Confirmed, check.Status);
            Assert.Equal(now, check.AgentJudgedAt!.Value, TimeSpan.FromSeconds(1));
            Assert.Null(check.AgentError);
            Assert.Equal(1, check.AgentAttempts);

            // THE STEP, on the latest workflow and nowhere else.
            var step = Assert.Single(await db.AgentSteps.AsNoTracking()
                .Where(s => s.AgentName == AgentRunResponse.VerificationAgentName).ToListAsync());
            Assert.Equal(latestWorkflow, step.WorkflowId);
            Assert.Equal("[]", step.ToolCallsJson);
            Assert.Equal("Ok", step.ValidationResult);
            Assert.Equal(1234, step.DurationMs);
            Assert.Equal(2, step.Attempts);
            using var payload = JsonDocument.Parse(step.PayloadJson!);
            Assert.Equal("escalate", payload.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("evidence").GetArrayLength());
            Assert.False(await db.AgentSteps.AnyAsync(s => s.WorkflowId == olderWorkflow));
        }

        // What a manager reads.
        var detail = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.Judged, detail.AgentState);
        Assert.Equal("escalate", detail.AgentOutcome);
        Assert.Equal(2, detail.AgentEvidence!.Count);

        // Judged, so no longer waiting: a second pass asks nothing.
        Assert.Equal(new VerificationAgentPassResult(0, 0, 0, 0), await PassAsync(factory));
        Assert.Single(factory.Agent.Requests);
    }

    [Fact]
    public async Task OnlyChecksWaitingOnTheAgent_AreSent_OldestQueuedFirst()
    {
        using var factory = new VerificationAgentApiFactory();
        var now = factory.Clock.Now.UtcDateTime;
        int queuedLater, queuedEarlier;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The lower id is queued LATER, so id order and queue order disagree.
            queuedLater = await SeedAsync(db, "VO1", now.AddHours(-1));
            queuedEarlier = await SeedAsync(db, "VO2", now.AddHours(-2));

            // Never queued, and queued-then-judged: neither is waiting.
            await SeedAsync(db, "VO3", queuedAt: null);
            await SeedAsync(db, "VO4", now.AddHours(-3), judgedAt: now.AddHours(-2), outcome: "confirm");
        }

        factory.Agent.Replies.Enqueue(Reply(Verdict("confirm")));
        factory.Agent.Replies.Enqueue(Reply(Verdict("reopen")));

        var pass = await PassAsync(factory);

        Assert.Equal(2, pass.Judged);
        Assert.Equal(2, factory.Agent.Requests.Count);

        using var check = factory.Services.CreateScope();
        var db2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderOf = await db2.VerificationChecks.AsNoTracking()
            .ToDictionaryAsync(v => v.Id, v => v.WorkOrderId);

        Assert.Equal(orderOf[queuedEarlier], factory.Agent.Requests[0].Verification!.WorkOrderId);
        Assert.Equal(orderOf[queuedLater], factory.Agent.Requests[1].Verification!.WorkOrderId);
    }

    /// <summary>
    /// A call the agent service never answered — unreachable, or a 200 with no verification in
    /// it — is retried on later passes, and stops at the bound with the reason, rather than
    /// being retried forever. Each attempt leaves its CallFailed step.
    /// </summary>
    [Fact]
    public async Task ACallThatFails_IsRetriedOnLaterPasses_AndGivenUpOnAtTheBound()
    {
        using var factory = new VerificationAgentApiFactory();
        int checkId;

        using (var scope = factory.Services.CreateScope())
        {
            checkId = await SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "VR1",
                factory.Clock.Now.UtcDateTime.AddMinutes(-1));
        }

        factory.Agent.Replies.Enqueue(Reply(NoEnvelope));

        for (var attempt = 2; attempt <= VerificationAgentRules.MaxAttempts; attempt++)
        {
            factory.Agent.Replies.Enqueue(new AgentCallResult(false, null, "The agent service did not respond within 360 seconds.", 360_000));
        }

        for (var attempt = 1; attempt < VerificationAgentRules.MaxAttempts; attempt++)
        {
            Assert.Equal(new VerificationAgentPassResult(0, 1, 0, 0), await PassAsync(factory));

            var retrying = await DetailAsync(factory, checkId);
            Assert.Equal(VerificationAgentState.Retrying, retrying.AgentState);
            Assert.Null(retrying.AgentJudgedAt);
            Assert.NotNull(retrying.AgentError);
        }

        Assert.Equal(new VerificationAgentPassResult(0, 0, 1, 0), await PassAsync(factory));

        var gaveUp = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.CouldNotJudge, gaveUp.AgentState);
        Assert.Null(gaveUp.AgentOutcome);
        Assert.StartsWith($"Gave up after {VerificationAgentRules.MaxAttempts} attempts.", gaveUp.AgentError);

        // THE BOUND: a further pass does not ask again.
        await PassAsync(factory);
        Assert.Equal(VerificationAgentRules.MaxAttempts, factory.Agent.Requests.Count);

        using var verify = factory.Services.CreateScope();
        var steps = await verify.ServiceProvider.GetRequiredService<AppDbContext>().AgentSteps.AsNoTracking()
            .Where(s => s.AgentName == AgentRunResponse.VerificationAgentName)
            .OrderBy(s => s.Id)
            .ToListAsync();

        Assert.Equal(VerificationAgentRules.MaxAttempts, steps.Count);
        Assert.All(steps, s => Assert.Equal("CallFailed", s.ValidationResult));
        Assert.Contains("carried no verification", steps[0].ErrorMessage);
        Assert.All(steps, s => Assert.Null(s.PayloadJson));
    }

    [Fact]
    public async Task ASafeFailure_IsFinal_AndNotRetried()
    {
        using var factory = new VerificationAgentApiFactory();
        int checkId;

        using (var scope = factory.Services.CreateScope())
        {
            checkId = await SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "VS1",
                factory.Clock.Now.UtcDateTime.AddMinutes(-1));
        }

        factory.Agent.Replies.Enqueue(Reply(SafeFailure));

        Assert.Equal(new VerificationAgentPassResult(0, 0, 1, 0), await PassAsync(factory));
        await PassAsync(factory);

        Assert.Single(factory.Agent.Requests);

        var detail = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.CouldNotJudge, detail.AgentState);
        Assert.Equal("The repair could not be looked up.", detail.AgentError);
        Assert.Null(detail.AgentOutcome);

        using var verify = factory.Services.CreateScope();
        var step = Assert.Single(await verify.ServiceProvider.GetRequiredService<AppDbContext>().AgentSteps
            .AsNoTracking().Where(s => s.AgentName == AgentRunResponse.VerificationAgentName).ToListAsync());
        Assert.Equal("SafeFailure", step.ValidationResult);
        Assert.Equal(40, step.DurationMs);
    }

    [Fact]
    public async Task ACheckWhoseReportHasNoWorkflow_IsGivenUpOn_WithoutACall()
    {
        using var factory = new VerificationAgentApiFactory();
        int checkId;

        using (var scope = factory.Services.CreateScope())
        {
            checkId = await SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "VN1",
                factory.Clock.Now.UtcDateTime.AddMinutes(-1), workflowState: null);
        }

        Assert.Equal(new VerificationAgentPassResult(0, 0, 1, 0), await PassAsync(factory));
        Assert.Empty(factory.Agent.Requests);

        var detail = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.CouldNotJudge, detail.AgentState);
        Assert.Contains("no agent workflow", detail.AgentError);
    }

    /// <summary>
    /// The late-answer question: a check queued as SILENT is judged, then the reporter answers.
    /// The answer queues it again, after its judgement, so it is judged again — with the
    /// answer — and the verdict on the silence is kept only on its own step.
    /// </summary>
    [Fact]
    public async Task ALateAnswer_IsJudgedAgain_WithTheAnswer()
    {
        using var factory = new VerificationAgentApiFactory();
        int checkId, reporterId, workflowId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, "VL1", factory.Clock.Now.UtcDateTime.AddDays(-12));
            reporterId = order.Report!.ReporterId;
            workflowId = await AddWorkflowAsync(db, order.ReportId, WorkflowState.AwaitingVerification);

            // Asked, never answered, and handed to the agent as silence.
            checkId = await AddCheckAsync(db, order, VerificationStatus.AwaitingReporterResponse,
                confirmed: null, comment: null, queuedAt: factory.Clock.Now.UtcDateTime.AddMinutes(-1));
        }

        factory.Agent.Replies.Enqueue(Reply(Verdict("confirm")));
        await PassAsync(factory);
        Assert.Null(factory.Agent.Requests[0].Verification!.ReporterConfirmed);
        Assert.Equal("confirm", (await DetailAsync(factory, checkId)).AgentOutcome);

        // An hour later the reporter answers: not fixed.
        factory.Clock.Now = factory.Clock.Now.AddHours(1);
        await AnswerAsync(factory, checkId, reporterId, fixedIt: false, "Cut out again on Monday.");

        var requeued = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.Queued, requeued.AgentState);
        Assert.Null(requeued.AgentOutcome);

        factory.Agent.Replies.Enqueue(Reply(Verdict("reopen")));
        Assert.Equal(1, (await PassAsync(factory)).Judged);

        var second = factory.Agent.Requests[1].Verification!;
        Assert.False(second.ReporterConfirmed);
        Assert.Equal("Cut out again on Monday.", second.ReporterComment);

        var judged = await DetailAsync(factory, checkId);
        Assert.Equal("reopen", judged.AgentOutcome);
        Assert.Equal(VerificationAgentState.Judged, judged.AgentState);

        // Both verdicts on the audit trail; the check carries the one about the answer.
        using var verify = factory.Services.CreateScope();
        var steps = await verify.ServiceProvider.GetRequiredService<AppDbContext>().AgentSteps.AsNoTracking()
            .Where(s => s.AgentName == AgentRunResponse.VerificationAgentName)
            .OrderBy(s => s.Id)
            .ToListAsync();
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.Equal(workflowId, s.WorkflowId));
        Assert.Contains("\"confirm\"", steps[0].PayloadJson);
    }

    /// <summary>
    /// The same late answer, arriving WHILE the agent is judging the silence. That verdict is
    /// about the silence, so it stays on its step and the check waits to be judged again —
    /// rather than the stale verdict being stamped as judged after the answer, and the answer
    /// never reaching the agent.
    /// </summary>
    [Fact]
    public async Task AnAnswerThatArrivesWhileTheAgentIsJudging_IsNotLost()
    {
        using var factory = new VerificationAgentApiFactory();
        int checkId, reporterId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, "VM1", factory.Clock.Now.UtcDateTime.AddDays(-12));
            reporterId = order.Report!.ReporterId;
            await AddWorkflowAsync(db, order.ReportId, WorkflowState.AwaitingVerification);
            checkId = await AddCheckAsync(db, order, VerificationStatus.AwaitingReporterResponse,
                confirmed: null, comment: null, queuedAt: factory.Clock.Now.UtcDateTime.AddMinutes(-1));
        }

        factory.Agent.DuringCall = async _ =>
        {
            factory.Agent.DuringCall = null;
            factory.Clock.Now = factory.Clock.Now.AddMinutes(2);
            await AnswerAsync(factory, checkId, reporterId, fixedIt: false, null);
        };
        factory.Agent.Replies.Enqueue(Reply(Verdict("confirm")));

        await PassAsync(factory);

        var afterFirst = await DetailAsync(factory, checkId);
        Assert.Equal(VerificationAgentState.Queued, afterFirst.AgentState);
        Assert.Null(afterFirst.AgentOutcome);

        factory.Agent.Replies.Enqueue(Reply(Verdict("reopen")));
        await PassAsync(factory);

        Assert.False(factory.Agent.Requests[1].Verification!.ReporterConfirmed);
        Assert.Equal("reopen", (await DetailAsync(factory, checkId)).AgentOutcome);
    }

    /// <summary>
    /// No request calls the agent — an answer or a sweep rings the runner's doorbell and returns.
    /// Without the ring a verdict would wait up to a sweep interval, which a demo cannot.
    /// </summary>
    [Fact]
    public async Task AReportersAnswer_AndTheSweep_WakeTheRunner()
    {
        using var factory = new VerificationAgentApiFactory();
        var signal = factory.Services.GetRequiredService<IVerificationAgentSignal>();
        int checkId, reporterId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, "VW1", factory.Clock.Now.UtcDateTime.AddDays(-12));
            reporterId = order.Report!.ReporterId;
            checkId = await AddCheckAsync(db, order, VerificationStatus.AwaitingReporterResponse,
                confirmed: null, comment: null, queuedAt: null);
        }

        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        await AnswerAsync(factory, checkId, reporterId, fixedIt: true, null);
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IVerificationService>().ProcessDueChecksAsync();
        }

        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    // -----------------------------------------------------------------------------------

    private static AgentCallResult Reply(string json, int durationMs = 5000) =>
        new(true, JsonSerializer.Deserialize<AgentRunResponse>(json)!, null, durationMs);

    private static async Task<VerificationAgentPassResult> PassAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IVerificationAgentService>().JudgeQueuedChecksAsync();
    }

    private static async Task AnswerAsync(ApiFactory factory, int checkId, int reporterId, bool fixedIt, string? comment)
    {
        using var scope = factory.Services.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IVerificationService>()
            .RecordReporterResponseAsync(checkId, reporterId, new ReporterConfirmationDto(fixedIt, comment));
        Assert.Equal(ConfirmVerificationOutcome.Success, outcome);
    }

    private static async Task<VerificationDetailDto> DetailAsync(ApiFactory factory, int checkId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IVerificationService>()
            .GetDetailAsync(checkId, callerId: 0, Role.FacilitiesManager))!;
    }

    /// <summary>
    /// A completed repair with its check — queued for the agent at <paramref name="queuedAt"/>
    /// (or never), judged at <paramref name="judgedAt"/> (or never) — and, unless
    /// <paramref name="workflowState"/> is null, a workflow for its report.
    /// </summary>
    private static async Task<int> SeedAsync(
        AppDbContext db,
        string prefix,
        DateTime? queuedAt,
        DateTime? judgedAt = null,
        string? outcome = null,
        WorkflowState? workflowState = WorkflowState.Closed)
    {
        var order = await VerificationTests.SeedCompletedWorkOrderAsync(db, prefix, (queuedAt ?? DateTime.UtcNow).AddDays(-8));

        if (workflowState is not null)
        {
            await AddWorkflowAsync(db, order.ReportId, workflowState.Value);
        }

        var id = await AddCheckAsync(db, order, VerificationStatus.Confirmed, confirmed: true, comment: null, queuedAt);

        if (judgedAt is not null)
        {
            var check = await db.VerificationChecks.SingleAsync(v => v.Id == id);
            check.AgentJudgedAt = judgedAt;
            check.AgentOutcome = outcome;
            await db.SaveChangesAsync();
        }

        return id;
    }

    private static async Task<int> AddCheckAsync(
        AppDbContext db, WorkOrder order, VerificationStatus status, bool? confirmed, string? comment, DateTime? queuedAt)
    {
        var check = new VerificationCheck
        {
            WorkOrderId = order.Id,
            AssetId = order.AssetId,
            DueAt = order.CompletedAt!.Value.AddDays(5),
            Status = status,
            ProcessedAt = order.CompletedAt.Value.AddDays(5),
            ReporterConfirmed = confirmed,
            ReporterComment = comment,
            ReporterRespondedAt = confirmed is null ? null : queuedAt,
            AgentQueuedAt = queuedAt
        };

        db.VerificationChecks.Add(check);
        await db.SaveChangesAsync();
        return check.Id;
    }

    /// <summary>A workflow for the report, CREATED in its state — test data, like WorkflowTestData.</summary>
    internal static async Task<int> AddWorkflowAsync(AppDbContext db, int reportId, WorkflowState state)
    {
        var workflow = new AgentWorkflow { ReportId = reportId, Objective = "Test run", CurrentState = state };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();
        return workflow.Id;
    }
}
