using System.Text.Json;
using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The background half of "POST returns 202". A BackgroundService is a long-running
/// IHostedService: the host starts it once at boot and it sits on the queue for the
/// lifetime of the process, so no request thread ever waits for agent work.
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
    /// Writes the one AGENT-LEVEL step for this run.
    ///
    /// Exactly one, and only agent-level: the tool calls the agent made on its way here
    /// already wrote their own AgentStep rows from InternalToolsController, which sees
    /// every call including the ones it rejects. Recording the agent's returned tool_calls
    /// here as well would double every tool call in the audit trail, so ToolCallsJson is
    /// the empty array and the controller stays the single owner of those rows.
    /// </summary>
    private static async Task RecordAgentStepAsync(
        IWorkflowService workflows,
        int workflowId,
        AgentCallResult call,
        CancellationToken cancellationToken)
    {
        var succeeded = call.Ok && call.Response is not null && !call.Response.IsSafeFailure;

        await workflows.RecordStepAsync(
            workflowId,
            agentName: call.Response?.Agent ?? "clarifier",
            toolCallsJson: "[]",
            // The agent's output verbatim, into the jsonb column that exists for exactly
            // this. Note what is NOT written: AgentWorkflow.PlanJson stays null, because
            // the clarifier produces questions and questions are not a plan. Putting them
            // there would mislabel them for every reader of that column.
            payloadJson: call.Response is null
                ? null
                : JsonSerializer.Serialize(call.Response.Output),
            durationMs: call.DurationMs,
            validationResult: succeeded ? "Ok" : call.Ok ? "SafeFailure" : "CallFailed",
            errorMessage: call.Error ?? call.Response?.Error,
            cancellationToken);
    }

    /// <summary>
    /// Writes the clarifier's questions as ClarificationQuestion rows — the working data
    /// beside the AgentStep audit row.
    ///
    /// A workflow with no report writes none. That is not a failure: POST /api/workflows
    /// can still start a run from a bare objective, and a question about nothing has
    /// nobody to ask and nowhere to appear. The AgentStep still records what was asked, so
    /// the run is not invisible.
    /// </summary>
    private async Task RecordClarificationQuestionsAsync(
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

            return;
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

        await clarifications.RecordQuestionsAsync(
            reportId.Value, workflowId, questions, cancellationToken);
    }

    private async Task ProcessAsync(int workflowId, CancellationToken cancellationToken)
    {
        // One scope per workflow: AppDbContext and IWorkflowService are scoped, and a
        // DbContext must never be shared across concurrent units of work.
        using var scope = _scopeFactory.CreateScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var clarifications = scope.ServiceProvider.GetRequiredService<IClarificationService>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentClient>();

        try
        {
            var started = await workflows.BeginProcessingAsync(workflowId, cancellationToken);

            if (!started)
            {
                // Either the row is gone or something already moved it past Submitted.
                _logger.LogWarning(
                    "Workflow {WorkflowId} was not in a startable state; skipping.", workflowId);
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

            var call = await agent.RunAsync(
                new AgentRunRequest(
                    WorkflowId: workflowId,
                    Description: workflow.Objective,
                    RoomId: report?.RoomId,
                    // The clarifier can look a building up, but nothing here knows which
                    // one: a report names a room, and resolving room -> building would be
                    // a query this runner has no reason to make. The agent can call
                    // get_room and read buildingId off the result if it needs it.
                    BuildingId: null),
                cancellationToken);

            await RecordAgentStepAsync(workflows, workflowId, call, cancellationToken);

            // Two different failures, one outcome. A call that never completed (timeout,
            // refused connection, bad body) and a call that completed with the agent
            // reporting it could not produce output both leave the workflow unable to
            // proceed, so both move it to Failed with the reason on the row rather than
            // leaving it parked in Diagnosing forever.
            if (!call.Ok || call.Response is null)
            {
                // call.Error is already a complete sentence from AgentClient; prefixing it
                // here produced "The agent service could not be reached: Could not reach
                // the agent service: ...", which a user reads on the workflow page.
                await workflows.FailAsync(
                    workflowId,
                    call.Error ?? "The agent service call failed for an unknown reason.",
                    cancellationToken);
                return;
            }

            if (call.Response.IsSafeFailure)
            {
                await workflows.FailAsync(
                    workflowId,
                    $"The clarifier could not produce questions: {call.Response.Error ?? "no reason given"}",
                    cancellationToken);
                return;
            }

            // The questions are now written TWICE, on purpose and to two different ends.
            // RecordAgentStepAsync above stored the agent's payload verbatim: that is the
            // audit trail and it is never edited. This stores the same questions as rows
            // the application can actually use — queried per report, ordered, rendered as
            // a form and answered. Neither replaces the other.
            await RecordClarificationQuestionsAsync(
                clarifications, workflow.ReportId, workflowId, call.Response, cancellationToken);

            await workflows.CompleteClarificationAsync(
                workflowId, call.Response.QuestionCount, cancellationToken);
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
                _logger.LogError(failureEx,
                    "Could not mark workflow {WorkflowId} as failed.", workflowId);
            }
        }
    }
}
