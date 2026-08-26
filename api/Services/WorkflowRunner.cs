using System.Diagnostics;
using System.Text.Json;

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

    private async Task ProcessAsync(int workflowId, CancellationToken cancellationToken)
    {
        // One scope per workflow: AppDbContext and IWorkflowService are scoped, and a
        // DbContext must never be shared across concurrent units of work.
        using var scope = _scopeFactory.CreateScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IWorkflowService>();

        var stopwatch = Stopwatch.StartNew();

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

            // ---------------------------------------------------------------------
            // This is where the call to the Python agent service will go: post the
            // objective, then let the agent call back into /api/internal/tools/* for
            // any data it needs. Until that service exists the runner only records the
            // hand-off, so the workflow parks in Diagnosing and the polling contract
            // (202, then GET) is already exercisable end to end.
            // ---------------------------------------------------------------------
            stopwatch.Stop();

            await workflows.RecordStepAsync(
                workflowId,
                agentName: "orchestrator",
                toolCallsJson: "[]",
                payloadJson: JsonSerializer.Serialize(new
                {
                    message = "Workflow accepted and handed off for diagnosis."
                }),
                durationMs: (int)stopwatch.ElapsedMilliseconds,
                validationResult: "Ok",
                errorMessage: null,
                cancellationToken);
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
