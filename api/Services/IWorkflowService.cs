using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IWorkflowService
{
    /// <summary>
    /// Creates the workflow row in state Submitted and returns immediately. It does not
    /// call the agent service — the controller queues the id and the background runner
    /// picks it up.
    /// </summary>
    Task<WorkflowSummaryDto> StartAsync(StartWorkflowRequest dto, CancellationToken cancellationToken = default);

    /// <summary>Returns null when no workflow has that id (a 404 for the caller).</summary>
    Task<WorkflowDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(
        WorkflowState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one audit row to the workflow. Returns false when the workflow does not
    /// exist, so nothing is silently written against a missing parent.
    /// </summary>
    Task<bool> RecordStepAsync(
        int workflowId,
        string agentName,
        string? toolCallsJson,
        string? payloadJson,
        int durationMs,
        string? validationResult,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the background runner once it dequeues a workflow: stamps StartedAt and
    /// moves Submitted to Diagnosing. Returns false when the workflow has vanished or is
    /// no longer in a state that can be started.
    /// </summary>
    Task<bool> BeginProcessingAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>Moves a workflow to Failed and records why. Used when the runner throws.</summary>
    Task<bool> FailAsync(int workflowId, string reason, CancellationToken cancellationToken = default);
}
