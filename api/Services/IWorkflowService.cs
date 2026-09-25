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
    /// Called by the background runner once it dequeues a workflow. Returns the state the
    /// run starts FROM — Submitted for a fresh report, Diagnosing for one whose reporter has
    /// answered the clarifier's questions — and stamps StartedAt the first time.
    ///
    /// Not a transition: the clarifier runs while the workflow is still Submitted, because
    /// what it says decides whether the next state is AwaitingClarification or Diagnosing.
    /// Null when the workflow has vanished or sits anywhere else — waiting on a person, or
    /// already past the agents — so a stray queue entry can never rewind it.
    /// </summary>
    Task<WorkflowState?> BeginProcessingAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a workflow to Failed and records why. Used when the agent call fails, when the
    /// clarifier safe-fails, and when the runner throws. Refused (an
    /// InvalidWorkflowTransitionException) from a state the runner does not own.
    /// </summary>
    Task<bool> FailAsync(int workflowId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clarifier has answered: Submitted to AwaitingClarification when it asked anything
    /// (human pause 1 — the run stops here), otherwise to Diagnosing. The choice is
    /// WorkflowTransitions.ForClarification, beside every other rule of the machine.
    /// </summary>
    Task<bool> CompleteClarificationAsync(
        int workflowId,
        int questionCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The diagnostic has run: Diagnosing to Strategizing, whether or not it produced a
    /// diagnosis. A failed diagnosis costs the strategist its evidence, not its turn — it is
    /// written to reason without one — and the failure is on the diagnostic's own step.
    /// </summary>
    Task<bool> CompleteDiagnosisAsync(
        int workflowId,
        bool diagnosed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The strategist has run. NOT a transition: the workflow stays in Strategizing until a
    /// manager raises the work order, and the order's approval gate decides whether that
    /// lands in WorkOrderRaised or AwaitingManagerApproval. The proposal is advice, so it
    /// moves nothing; only the Outcome line says what the workflow is waiting for.
    /// </summary>
    Task<bool> RecordProposalAsync(
        int workflowId,
        bool proposed,
        CancellationToken cancellationToken = default);
}
