using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The background half of "POST returns 202". A BackgroundService is a long-running
/// IHostedService: the host starts it once at boot and it sits on the queue for the
/// lifetime of the process, so no request thread ever waits for agent work.
///
/// A fresh run starts from its PLAN. The planner agent reads the objective and delegates
/// the run to the clarifier (when the report needs it), the diagnostic and the strategist;
/// PlanRules re-checks that plan in C# before it is stored as AgentWorkflow.PlanJson, and a
/// plan that fails the check — or a planner that fails — is replaced by the fallback plan,
/// with the reason. Each plan step is then marked as its agent's result is recorded.
///
/// It advances a workflow ONE AGENT AT A TIME through the state machine
/// (WorkflowTransitions): the planner's step, then the clarifier's step and its transition,
/// then the diagnostic's step and its transition, then the strategist's. Each save stands on
/// its own, so a poll of GET /api/workflows/{id} sees the run move agent by agent.
///
/// It STOPS at the two human pauses and never waits in them. AwaitingClarification ends the
/// run; the reporter's answers re-queue the id and the run resumes from Diagnosing. A repair
/// that verification reopens resumes from Diagnosing too, and the diagnostic runs again.
/// AwaitingManagerApproval is never reached by the runner at all — the workflow waits in
/// Strategizing with the proposal until a manager raises the order, and the order's gate
/// decides. Nothing here blocks on a person; the queue simply has nothing for it.
///
/// One /run call per segment between pauses, not per agent: the agent service runs the
/// graph's agents in order inside one call and returns every result (graph.py routes it).
/// The per-agent audit and transitions happen here, from those results, in graph order.
/// </summary>
public class WorkflowRunner : BackgroundService
{
    private readonly IWorkflowQueue _queue;

    // A hosted service is a singleton, so it cannot take IWorkflowService (scoped)
    // directly — that is exactly the captive dependency the conventions warn about.
    // It takes the scope factory instead and opens one scope per workflow.
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ILogger<WorkflowRunner> _logger;

    public WorkflowRunner(
        IWorkflowQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowRunner> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow runner started.");

        // Not awaited before the loop: the queue is bounded, so re-queueing more ids than it
        // holds would wait for room that only this loop's dequeues can make. It runs beside the
        // loop instead, and catches everything itself.
        _ = RequeueUnfinishedRunsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            int workflowId;

            try
            {
                workflowId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, not a fault.
                break;
            }

            await ProcessAsync(workflowId, stoppingToken);
        }

        _logger.LogInformation("Workflow runner stopped.");
    }

    /// <summary>
    /// The queue is an in-process channel, so a restart empties it — and a host that sleeps
    /// when idle restarts often. The rows are durable, so at startup every workflow an agent
    /// run was meant to be working on (Submitted or Diagnosing) is queued again rather than
    /// left looking busy forever. Safe to repeat: the runner only starts from those two
    /// states, so an id that has already moved on is skipped when it comes round.
    /// </summary>
    internal async Task RequeueUnfinishedRunsAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<int> ids;

            using (var scope = _scopeFactory.CreateScope())
            {
                ids = await scope.ServiceProvider.GetRequiredService<IWorkflowService>()
                    .GetUnfinishedRunIdsAsync(cancellationToken);
            }

            foreach (var id in ids)
            {
                await _queue.EnqueueAsync(id, cancellationToken);
            }

            if (ids.Count > 0)
            {
                _logger.LogInformation(
                    "Re-queued {Count} workflow(s) left unfinished by the last shutdown.", ids.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down before the re-queue finished; the rows are still there next time.
        }
        catch (Exception ex)
        {
            // Most often the database is not reachable yet. The runner still serves new work.
            _logger.LogError(ex, "Could not re-queue unfinished workflows at startup.");
        }
    }

    /// <summary>
    /// How long each agent in one /run call took. The agent service reports every agent's
    /// own duration_ms, and that is what each step records. When it does not — a reply from
    /// an agent service that predates the field — the FIRST agent recorded carries the whole
    /// call's time and the rest 0, because dividing the total would invent a split.
    /// </summary>
    private sealed class CallTiming
    {
        private readonly int _wholeCallMs;
        private bool _wholeCallSpent;

        public CallTiming(int wholeCallMs) => _wholeCallMs = wholeCallMs;

        public int For(int? reportedMs)
        {
            if (reportedMs is int ms)
            {
                return ms;
            }

            if (_wholeCallSpent)
            {
                return 0;
            }

            _wholeCallSpent = true;
            return _wholeCallMs;
        }
    }

    /// <summary>
    /// Writes the clarifier's AGENT-LEVEL step for this run — and, when the call never
    /// completed, the only step: a CallFailed row with the reason.
    ///
    /// Only agent-level: the tool calls the agent made on its way here already wrote their
    /// own AgentStep rows from InternalToolsController, which sees every call including the
    /// ones it rejects. Recording the agent's returned tool_calls here as well would double
    /// every tool call in the audit trail, so ToolCallsJson is the empty array and the
    /// controller stays the single owner of those rows.
    /// </summary>
    private static async Task RecordAgentStepAsync(
        IWorkflowService workflows,
        int workflowId,
        AgentCallResult call,
        CallTiming timing,
        CancellationToken cancellationToken)
    {
        var succeeded = call.Ok && call.Response is not null && !call.Response.IsSafeFailure;

        await workflows.RecordStepAsync(
            workflowId,
            agentName: call.Response?.Agent ?? AgentRunResponse.ClarifierAgentName,
            toolCallsJson: "[]",
            // The agent's output verbatim, into the jsonb column that exists for exactly
            // this. The clarifier's questions are NOT a plan — the plan is the planner's, and
            // is stored as PlanJson by RecordPlanAsync.
            payloadJson: call.Response is null
                ? null
                : JsonSerializer.Serialize(call.Response.Output),
            durationMs: timing.For(call.Response?.DurationMs),
            validationResult: succeeded ? "Ok" : call.Ok ? "SafeFailure" : "CallFailed",
            errorMessage: call.Error ?? call.Response?.Error,
            attempts: call.Response?.Attempts,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// One agent-level step for the diagnostic or the strategist, beside the clarifier's, so a
    /// diagnosis and a proposal are kept rather than computed and thrown away. The output goes
    /// in verbatim — the audit copy, never edited — and the approval queue reads it back from
    /// here (see AgentAnalysis). Same "[]" ToolCallsJson as the clarifier's step, for the same
    /// reason: their tool calls are already rows of their own.
    /// </summary>
    private static Task RecordDownstreamStepAsync(
        IWorkflowService workflows,
        int workflowId,
        DownstreamAgentResult result,
        CallTiming timing,
        CancellationToken cancellationToken) =>
        workflows.RecordStepAsync(
            workflowId,
            agentName: result.AgentName,
            toolCallsJson: "[]",
            payloadJson: result.OutputJson,
            durationMs: timing.For(result.DurationMs),
            validationResult: result.Succeeded ? "Ok" : "SafeFailure",
            errorMessage: result.Error,
            attempts: result.Attempts,
            cancellationToken: cancellationToken);

    /// <summary>
    /// The planner's step and the workflow's stored plan — first, because every other step of
    /// the run was delegated from it.
    ///
    /// The planner's reply is recorded VERBATIM on its own AgentStep, whatever it said: that is
    /// the audit copy. What is stored as PlanJson is different — the plan PlanRules has
    /// re-checked, or the fallback plan with the reason it was needed:
    ///
    ///   * the planner produced a plan that passes the C# check      → its plan, step "Ok";
    ///   * the planner safe-failed                                    → fallback, "SafeFailure";
    ///   * the planner produced a plan that FAILS the C# check        → fallback, "Rejected";
    ///   * the reply carried no plan (an older agent service)         → fallback, no step.
    ///
    /// A plan the model wrote is never stored as-is: the check is deterministic C#, the same
    /// rule on this side of the network as the Pydantic schema on the other.
    /// </summary>
    private async Task<WorkflowPlanDto> RecordPlanAsync(
        IWorkflowService workflows,
        int workflowId,
        AgentRunResponse response,
        CallTiming timing,
        CancellationToken cancellationToken)
    {
        var planner = response.PlannerResult();
        WorkflowPlanDto plan;

        if (planner is null)
        {
            plan = PlanRules.Fallback("The agent service returned no plan, so the run followed the default one.");
        }
        else
        {
            string validationResult;
            var error = planner.Error;

            if (!planner.Succeeded)
            {
                plan = PlanRules.Fallback(
                    $"The planner could not produce a plan ({planner.Error ?? "no reason given"}), " +
                    "so the run followed the default one.");
                validationResult = "SafeFailure";
            }
            else
            {
                var (checkedPlan, reason) = PlanRules.Validate(planner.Output);

                if (checkedPlan is null)
                {
                    // Should be impossible — the agent validates the same rules before it acts
                    // on a plan — which is exactly why it is worth a warning: the two contracts
                    // have drifted apart.
                    _logger.LogWarning(
                        "Workflow {WorkflowId}: the planner's plan failed the C# check ({Reason}). "
                        + "The fallback plan was stored instead.", workflowId, reason);

                    plan = PlanRules.Fallback($"The planner's plan was rejected: {reason}");
                    validationResult = "Rejected";
                    error = reason;
                }
                else
                {
                    plan = checkedPlan;
                    validationResult = "Ok";
                }
            }

            await workflows.RecordStepAsync(
                workflowId,
                agentName: PlanRules.PlannerAgentName,
                toolCallsJson: "[]",
                payloadJson: planner.OutputJson,
                durationMs: timing.For(planner.DurationMs),
                validationResult: validationResult,
                errorMessage: error,
                attempts: planner.Attempts,
                cancellationToken: cancellationToken);
        }

        await workflows.SetPlanAsync(workflowId, plan, cancellationToken);
        return plan;
    }

    /// <summary>
    /// Writes the clarifier's questions as ClarificationQuestion rows — the working data
    /// beside the AgentStep audit row — and returns how many were written.
    ///
    /// A workflow with no report writes none. That is not a failure: POST /api/workflows
    /// can still start a run from a bare objective, and a question about nothing has
    /// nobody to ask and nowhere to appear. The AgentStep still records what was asked, so
    /// the run is not invisible.
    /// </summary>
    private async Task<int> RecordClarificationQuestionsAsync(
        IClarificationService clarifications,
        int? reportId,
        int workflowId,
        AgentRunResponse response,
        CancellationToken cancellationToken)
    {
        if (reportId is null)
        {
            if (response.QuestionCount > 0)
            {
                _logger.LogInformation(
                    "Workflow {WorkflowId} has no report, so its {QuestionCount} clarifier "
                    + "question(s) were recorded on the agent step only.",
                    workflowId, response.QuestionCount);
            }

            return 0;
        }

        var questions = response.ParseQuestions();

        // ParseQuestions skips anything malformed rather than throwing, so a shortfall here
        // is the only sign that it happened. Worth a warning: the agent validates its own
        // output against a Pydantic schema before sending it, so this should be impossible
        // and means the two contracts have drifted apart.
        if (questions.Count != response.QuestionCount)
        {
            _logger.LogWarning(
                "Workflow {WorkflowId}: the agent returned {ReturnedCount} question(s) but only "
                + "{ParsedCount} could be read. The full payload is on the agent step.",
                workflowId, response.QuestionCount, questions.Count);
        }

        return await clarifications.RecordQuestionsAsync(
            reportId.Value, workflowId, questions, cancellationToken);
    }

    /// <summary>
    /// One dequeued workflow, start to pause. Internal rather than private so
    /// WorkflowRunnerTests can drive a single run without starting the background loop — the
    /// same reason SlotRules is internal.
    /// </summary>
    internal async Task ProcessAsync(int workflowId, CancellationToken cancellationToken)
    {
        // One scope per workflow: AppDbContext and IWorkflowService are scoped, and a
        // DbContext must never be shared across concurrent units of work.
        using var scope = _scopeFactory.CreateScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();
        var workOrders = scope.ServiceProvider.GetRequiredService<IWorkOrderService>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentClient>();

        try
        {
            var from = await workflows.BeginProcessingAsync(workflowId, cancellationToken);

            if (from is null)
            {
                // Gone, waiting on a person, or past the agents. A revision re-queues its
                // workflow in Strategizing and lands here too: re-running the strategist on
                // its own is not wired yet (see WorkOrderService.RequestRevisionAsync).
                _logger.LogWarning(
                    "Workflow {WorkflowId} was not in a state the runner starts from; skipping.", workflowId);
                return;
            }

            var workflow = await workflows.GetByIdAsync(workflowId, cancellationToken);

            if (workflow is null)
            {
                // Deleted between BeginProcessingAsync and here. Nothing to fail against.
                _logger.LogWarning("Workflow {WorkflowId} disappeared while starting.", workflowId);
                return;
            }

            // The room gives the clarifier somewhere to look: it is what the agent passes
            // to the get_room tool. A workflow started from a bare objective has no report
            // and therefore no room, which is fine — the agent works from the text alone.
            var report = workflow.ReportId is null
                ? null
                : await reports.GetByIdAsync(workflow.ReportId.Value, cancellationToken);

            if (from == WorkflowState.Submitted)
            {
                await RunFromSubmittedAsync(workflows, clarifications, agent, workflow, report, cancellationToken);
            }
            else
            {
                await ResumeAtDiagnosisAsync(workflows, clarifications, workOrders, agent, workflow, report, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // A background exception has no request to surface on, so it is logged and
            // written to the workflow itself — a poll must never hang on a dead run.
            _logger.LogError(ex, "Workflow {WorkflowId} failed.", workflowId);

            try
            {
                await workflows.FailAsync(workflowId, "The workflow failed while processing.", cancellationToken);
            }
            catch (Exception failureEx)
            {
                // Including an illegal transition: if the workflow already reached a pause,
                // Failed is not a move it may make, and the pause is the truer state.
                _logger.LogError(failureEx,
                    "Could not mark workflow {WorkflowId} as failed.", workflowId);
            }
        }
    }

    /// <summary>
    /// A fresh report: the planner first, then whatever it delegated — the clarifier when the
    /// report needs one, and (when nothing needs asking) the diagnostic and the strategist,
    /// which graph.py runs in the same call.
    /// </summary>
    private async Task RunFromSubmittedAsync(
        IWorkflowService workflows,
        IClarificationService clarifications,
        IAgentClient agent,
        WorkflowDetailDto workflow,
        ReportDto? report,
        CancellationToken cancellationToken)
    {
        var call = await agent.RunAsync(
            RequestFor(workflow, report, answers: null, assetId: report?.AssetId, reopened: false),
            cancellationToken);

        var timing = new CallTiming(call.DurationMs);

        // A call that never completed (timeout, refused connection, bad body) ran nothing and
        // planned nothing: its one step says why, and the run is Failed with the reason on the
        // row rather than left parked in Submitted.
        if (!call.Ok || call.Response is null)
        {
            await RecordAgentStepAsync(workflows, workflow.Id, call, timing, cancellationToken);

            // call.Error is already a complete sentence from AgentClient; prefixing it
            // here produced "The agent service could not be reached: Could not reach
            // the agent service: ...", which a user reads on the workflow page.
            await workflows.FailAsync(
                workflow.Id,
                call.Error ?? "The agent service call failed for an unknown reason.",
                cancellationToken);
            return;
        }

        var response = call.Response;

        // The plan comes first: it is what the rest of the run was delegated from.
        var plan = await RecordPlanAsync(workflows, workflow.Id, response, timing, cancellationToken);

        if (!response.ClarifierRan)
        {
            // The planner judged the report clear enough to go straight to diagnosis. When the
            // STORED plan still names the clarifier — the C# check refused the planner's plan
            // after the agent service had already followed it — the two disagree, and the
            // clarifier's step says so rather than sitting pending forever.
            if (PlanRules.IncludesClarifier(plan))
            {
                _logger.LogWarning(
                    "Workflow {WorkflowId}: the stored plan includes the clarifier, but the agent "
                    + "service went straight to the diagnostic.", workflow.Id);

                await workflows.MarkPlanStepAsync(
                    workflow.Id, AgentRunResponse.ClarifierAgentName, PlanStepStatus.Skipped, cancellationToken);
            }

            await workflows.ProceedWithoutClarificationAsync(workflow.Id, cancellationToken);
            await AdvanceThroughDownstreamAsync(workflows, workflow.Id, response, timing, cancellationToken);
            return;
        }

        await RecordAgentStepAsync(workflows, workflow.Id, call, timing, cancellationToken);

        // A clarifier that reported it could not produce output leaves the run unable to
        // decide whether to pause, so it is Failed with the reason on the row.
        if (response.IsSafeFailure)
        {
            await workflows.MarkPlanStepAsync(
                workflow.Id, AgentRunResponse.ClarifierAgentName, PlanStepStatus.Failed, cancellationToken);
            await workflows.FailAsync(
                workflow.Id,
                $"The clarifier could not produce questions: {response.Error ?? "no reason given"}",
                cancellationToken);
            return;
        }

        // The questions are now written TWICE, on purpose and to two different ends.
        // RecordAgentStepAsync above stored the agent's payload verbatim: that is the
        // audit trail and it is never edited. This stores the same questions as rows
        // the application can actually use — queried per report, ordered, rendered as
        // a form and answered. Neither replaces the other.
        var written = await RecordClarificationQuestionsAsync(
            clarifications, workflow.ReportId, workflow.Id, response, cancellationToken);

        await workflows.MarkPlanStepAsync(
            workflow.Id, AgentRunResponse.ClarifierAgentName, PlanStepStatus.Completed, cancellationToken);

        // Questions were asked about a report, but none could be recorded against it —
        // unreadable, or the report's lifecycle refused the move back to AwaitingClarification.
        // Pausing now would wait on a reporter who has nothing to answer.
        if (response.QuestionCount > 0 && workflow.ReportId is not null && written == 0)
        {
            await workflows.FailAsync(
                workflow.Id,
                $"The clarifier asked {response.QuestionCount} question(s), but none could be recorded "
                + $"against report {workflow.ReportId}. The report may be past clarification.",
                cancellationToken);
            return;
        }

        await workflows.CompleteClarificationAsync(
            workflow.Id, response.QuestionCount, cancellationToken);

        if (response.QuestionCount > 0)
        {
            // HUMAN PAUSE 1. The run ends here, and graph.py stopped at the clarifier for
            // the same reason: a diagnosis made before the answers would be made without
            // the detail the clarifier just said it needed.
            return;
        }

        await AdvanceThroughDownstreamAsync(workflows, workflow.Id, response, timing, cancellationToken);
    }

    /// <summary>
    /// The run resumes at the diagnostic, for one of two reasons. Both leave the workflow in
    /// Diagnosing, and ReopenedWorkOrderId is what tells them apart:
    ///
    ///   * the reporter answered, and ClarificationService moved it here — the answers go out
    ///     as clarification_answers;
    ///   * the reporter said a repair did not hold, and VerificationService moved it here
    ///     (RepairReopened) — the request says reopened, and the plan gains a second
    ///     diagnostic and strategist step.
    ///     The diagnostic's tools then read the service history as it is NOW, with the record
    ///     the repair appended, and any report filed since.
    ///
    /// Either way graph.py goes straight to the diagnostic: the clarifier is not asked again
    /// about a report it already asked about, nor about a fault somebody already repaired.
    /// The diagnosis and the proposal are APPENDED as new steps, so a re-diagnosis sits beside
    /// the first one rather than replacing it.
    /// </summary>
    private async Task ResumeAtDiagnosisAsync(
        IWorkflowService workflows,
        IClarificationService clarifications,
        IWorkOrderService workOrders,
        IAgentClient agent,
        WorkflowDetailDto workflow,
        ReportDto? report,
        CancellationToken cancellationToken)
    {
        // THIS run's questions only: a report may have been clarified by an earlier run, and
        // those answers were given to other questions. On a re-diagnosis they are sent again:
        // they are still the reporter's own account of the fault that came back.
        var answers = workflow.ReportId is null
            ? new List<AgentClarificationAnswer>()
            : (await clarifications.GetForReportAsync(workflow.ReportId.Value, cancellationToken))
                .Where(q => q.WorkflowId == workflow.Id && q.AnswerText is not null)
                .OrderBy(q => q.DisplayOrder)
                .Select(q => new AgentClarificationAnswer(q.QuestionText, q.AnswerText!))
                .ToList();

        var reopened = workflow.ReopenedWorkOrderId is not null;

        if (answers.Count == 0 && !reopened)
        {
            // Sent with neither, the graph would run the clarifier again and ask the same
            // questions — the loop the answers exist to break. Refused rather than run.
            await workflows.FailAsync(
                workflow.Id,
                "The workflow was resumed for diagnosis but has no answered clarification questions.",
                cancellationToken);
            return;
        }

        // The report names the asset once triage or a QR scan has; the reopened work order
        // ALWAYS does (WorkOrder.AssetId is not nullable). Without this, a report whose asset
        // was never filled in would be re-diagnosed with no service history at all — and the
        // history, with the failed repair now on it, is the whole point of running again.
        var assetId = report?.AssetId;

        if (reopened)
        {
            // Delegated in the plan before it runs, so a poll sees the new steps pending.
            await workflows.AppendRediagnosisToPlanAsync(
                workflow.Id, workflow.ReopenedWorkOrderId!.Value, cancellationToken);

            if (assetId is null)
            {
                var order = await workOrders.GetWorkOrderFactsAsync(workflow.ReopenedWorkOrderId!.Value, cancellationToken);
                assetId = order?.AssetId;
            }
        }

        var call = await agent.RunAsync(
            RequestFor(workflow, report, answers.Count == 0 ? null : answers, assetId, reopened),
            cancellationToken);

        if (!call.Ok || call.Response is null)
        {
            // No clarifier ran, so there is no clarifier step to write — the reason is on
            // the workflow row, where a poll reads it.
            await workflows.FailAsync(
                workflow.Id,
                call.Error ?? "The agent service call failed for an unknown reason.",
                cancellationToken);
            return;
        }

        await AdvanceThroughDownstreamAsync(
            workflows, workflow.Id, call.Response, new CallTiming(call.DurationMs), cancellationToken);
    }

    /// <summary>
    /// The diagnostic, then the strategist: each one's step written, its plan step marked, then
    /// its transition made, in graph order. The workflow is in Diagnosing on the way in and in
    /// Strategizing on the way out, waiting for a manager to raise the order.
    ///
    /// An agent that safe-failed still moves the workflow on — the failure is on its step and
    /// its plan step, and a missing diagnosis or proposal costs advice, not the ability to
    /// raise an order. An agent that is ABSENT from the reply is different: the graph did not
    /// do what this runner was promised, so the run is Failed with that said.
    /// </summary>
    private static async Task AdvanceThroughDownstreamAsync(
        IWorkflowService workflows,
        int workflowId,
        AgentRunResponse response,
        CallTiming timing,
        CancellationToken cancellationToken)
    {
        var results = response.DownstreamResults();

        var diagnosis = results.FirstOrDefault(r => r.AgentName == AgentRunResponse.DiagnosticAgentName);

        if (diagnosis is null)
        {
            await workflows.FailAsync(workflowId, "The agent service returned no diagnosis.", cancellationToken);
            return;
        }

        await RecordDownstreamStepAsync(workflows, workflowId, diagnosis, timing, cancellationToken);
        await workflows.MarkPlanStepAsync(
            workflowId, AgentRunResponse.DiagnosticAgentName,
            diagnosis.Succeeded ? PlanStepStatus.Completed : PlanStepStatus.Failed, cancellationToken);
        await workflows.CompleteDiagnosisAsync(workflowId, diagnosis.Succeeded, cancellationToken);

        var proposal = results.FirstOrDefault(r => r.AgentName == AgentRunResponse.StrategistAgentName);

        if (proposal is null)
        {
            await workflows.FailAsync(workflowId, "The agent service returned no proposal.", cancellationToken);
            return;
        }

        await RecordDownstreamStepAsync(workflows, workflowId, proposal, timing, cancellationToken);
        await workflows.MarkPlanStepAsync(
            workflowId, AgentRunResponse.StrategistAgentName,
            proposal.Succeeded ? PlanStepStatus.Completed : PlanStepStatus.Failed, cancellationToken);
        await workflows.RecordProposalAsync(workflowId, proposal.Succeeded, cancellationToken);
    }

    private static AgentRunRequest RequestFor(
        WorkflowDetailDto workflow,
        ReportDto? report,
        IReadOnlyList<AgentClarificationAnswer>? answers,
        int? assetId,
        bool reopened) =>
        new(
            WorkflowId: workflow.Id,
            Description: workflow.Objective,
            RoomId: report?.RoomId,
            // The clarifier can look a building up, but nothing here knows which one: a
            // report names a room, and resolving room -> building would be a query this
            // runner has no reason to make. The agent can call get_room and read buildingId
            // off the result if it needs it.
            BuildingId: null,
            // Set when triage or a QR scan named the equipment, or — on a re-diagnosis — by
            // the reopened work order. It is what lets the diagnostic and the strategist read
            // the machine's service history.
            AssetId: assetId,
            ClarificationAnswers: answers,
            Reopened: reopened);
}
