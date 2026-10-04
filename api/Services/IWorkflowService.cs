using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IWorkflowService
{
    /// <summary>
    /// Creates the workflow row in state Submitted and returns immediately. It does not
    /// call the agent service — the caller queues the id and the background runner picks
    /// it up.
    ///
    /// Refused, with nothing written, when the request names a report that:
    ///   * does not exist (ReportNotFound, a 400 — ReportId is a real foreign key, so an
    ///     unchecked bad id would surface as a constraint violation out of the driver);
    ///   * is Closed (ReportClosed, a 409 — a fault that returns is a new report);
    ///   * already has a LIVE run (RunInProgress, a 409). One report, one live run: the
    ///     manager's actions move the report's LATEST workflow, so a second run started
    ///     beside a live one would quietly take its place. A new run is allowed once the
    ///     latest one has ended in Failed or Closed — "run the agents again".
    /// </summary>
    Task<StartWorkflowResult> StartAsync(StartWorkflowRequest dto, CancellationToken cancellationToken = default);

    /// <summary>Returns null when no workflow has that id (a 404 for the caller).</summary>
    Task<WorkflowDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(
        WorkflowState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>The workflow's current state, or null when no workflow has that id.</summary>
    Task<WorkflowState?> GetStateAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one audit row to the workflow. Returns false when the workflow does not
    /// exist, so nothing is silently written against a missing parent.
    /// <paramref name="attempts"/> is the LLM attempts behind an agent-level step (null for a
    /// tool call).
    /// </summary>
    Task<bool> RecordStepAsync(
        int workflowId,
        string agentName,
        string? toolCallsJson,
        string? payloadJson,
        int durationMs,
        string? validationResult,
        string? errorMessage,
        int? attempts = null,
        AgentTokenUsage? usage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the background runner once it dequeues a workflow. Returns the state the
    /// run starts FROM — Submitted for a fresh report, Diagnosing for one whose reporter has
    /// answered the clarifier's questions or said a repair did not hold — and stamps
    /// StartedAt the first time.
    ///
    /// Not a transition: the clarifier runs while the workflow is still Submitted, because
    /// what it says decides whether the next state is AwaitingClarification or Diagnosing.
    /// Null when the workflow has vanished or sits anywhere else — waiting on a person, or
    /// already past the agents — so a stray queue entry can never rewind it.
    ///
    /// Strategizing is returned too, for a REVISION: a manager sent the order back and the
    /// strategist runs again. Only the runner can tell whether a revision is pending there —
    /// it asks IWorkOrderService — and it skips any other Strategizing.
    /// </summary>
    Task<WorkflowState?> BeginProcessingAsync(int workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The workflows an agent run was meant to be working on: Submitted (not yet run) and
    /// Diagnosing (resumed). The queue is in memory, so a restart empties it; the runner
    /// re-queues these at startup so a restart does not strand them. Oldest first.
    /// </summary>
    Task<IReadOnlyList<int>> GetUnfinishedRunIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the workflow's structured plan in PlanJson — the only writer of that column.
    /// The plan has already been through PlanRules: the planner's, re-checked, or the fallback.
    /// </summary>
    Task<bool> SetPlanAsync(int workflowId, WorkflowPlanDto plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the first pending plan step for <paramref name="agentName"/> with
    /// <paramref name="status"/> (PlanStepStatus). A workflow with no stored plan is left alone.
    /// </summary>
    Task<bool> MarkPlanStepAsync(
        int workflowId,
        string agentName,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a second diagnostic and strategist step to the plan when a repair did not hold,
    /// so the re-diagnosis is delegated in the plan as well as recorded in the steps.
    /// </summary>
    Task<bool> AppendRediagnosisToPlanAsync(
        int workflowId,
        int reopenedWorkOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a strategist step to the plan when a manager sent the order back for revision,
    /// so the second proposal is delegated in the plan as well as recorded in the steps.
    /// </summary>
    Task<bool> AppendRevisionToPlanAsync(
        int workflowId,
        int workOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A revision the runner could not turn into a resubmitted order — the strategist call
    /// failed, or its proposal was unusable. NOT a transition: the workflow stays in
    /// Strategizing with the Draft, and the Outcome says a manager must resubmit it.
    /// </summary>
    Task<bool> RecordRevisionWaitingAsync(
        int workflowId,
        int workOrderId,
        string why,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The planner left the clarifier out of the plan: Submitted to Diagnosing, through its
    /// own trigger (PlannedWithoutClarification), not the clarifier's.
    /// </summary>
    Task<bool> ProceedWithoutClarificationAsync(int workflowId, CancellationToken cancellationToken = default);

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
    /// The strategist has run. NOT a transition: the workflow stays in Strategizing until the
    /// order is raised — by the runner from a usable proposal, or by a manager — and the
    /// order's approval gate decides whether that lands in WorkOrderRaised or
    /// AwaitingManagerApproval. The proposal is advice, so it
    /// moves nothing; only the Outcome line says what the workflow is waiting for.
    /// </summary>
    Task<bool> RecordProposalAsync(
        int workflowId,
        bool proposed,
        CancellationToken cancellationToken = default);
}

/// <summary>Why <see cref="IWorkflowService.StartAsync"/> did or did not start a run.</summary>
public enum StartWorkflowOutcome
{
    Started,
    ReportNotFound,
    ReportClosed,
    RunInProgress
}

public record StartWorkflowResult(StartWorkflowOutcome Outcome, WorkflowSummaryDto? Workflow = null);
