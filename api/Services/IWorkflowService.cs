using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IWorkflowService
{
    /// <summary>
    /// Creates the workflow row in state Submitted and returns immediately. It does not
    /// call the agent service — the controller queues the id and the background runner
    /// picks it up.
    ///
    /// Returns null when the request names a ReportId that does not exist (a 400 for the
    /// caller). ReportId is a real foreign key, so an unchecked bad id would surface as a
    /// constraint violation out of the driver rather than as a validation failure.
    /// </summary>
    Task<WorkflowSummaryDto?> StartAsync(StartWorkflowRequest dto, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Called by the background runner once the clarifier has answered. Decides where the
    /// workflow goes next from the number of questions it asked.
    ///
    /// The rule lives here, next to the other transitions, because which state follows
    /// which is a deterministic business rule and belongs in C# — never in a prompt, and
    /// not scattered through the runner either.
    /// </summary>
    Task<bool> CompleteClarificationAsync(
        int workflowId,
        int questionCount,
        CancellationToken cancellationToken = default);
}
