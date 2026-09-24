using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IVerificationService
{
    /// <summary>
    /// Raises the verification check for a work order that has just completed, with DueAt
    /// set to its completion plus the configured delay.
    ///
    /// Returns null for three different reasons — the work order does not exist, it is not
    /// Completed, or it already has an open check — because there is no Result wrapper in
    /// this project. A controller that needs to tell a 409 from a 400 calls
    /// <see cref="HasOpenCheckAsync"/> first, the same way InternalToolsController calls
    /// IWorkflowService.ExistsAsync before choosing its status code.
    /// </summary>
    Task<VerificationCheckDto?> CreateForCompletedWorkOrderAsync(
        int workOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this work order already has a check that has not reached a terminal state.
    /// See <see cref="CreateForCompletedWorkOrderAsync"/>.
    /// </summary>
    Task<bool> HasOpenCheckAsync(int workOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks, newest first, optionally filtered by state. The opposite order to the asset
    /// registry's service history: this is a worklist, so the most recent matters most.
    /// </summary>
    Task<IReadOnlyList<VerificationCheckDto>> GetAllAsync(
        VerificationStatusFilter filter = VerificationStatusFilter.All,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One check with its asset and the work order claim it is testing. Null when no check
    /// has that id (a 404 for the caller).
    /// </summary>
    Task<VerificationDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Every check raised against a work order, oldest first.</summary>
    Task<IReadOnlyList<VerificationCheckDto>> GetForWorkOrderAsync(
        int workOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// THE SWEEP — one pass of it. Two steps, each a deterministic rule in C#:
    ///
    ///   1. Every Pending check whose DueAt has passed moves to AwaitingReporterResponse,
    ///      with ProcessedAt stamped as the moment the reporter was asked.
    ///   2. Every check not yet queued for the verification agent is queued (AgentQueuedAt)
    ///      if the reporter has answered it, or if it has sat in AwaitingReporterResponse
    ///      longer than VerificationSettings.ResponseWindowDays since they were asked.
    ///
    /// ONE ROW AT A TIME, each saved on its own, so a row that throws cannot take the rest
    /// down with it. The failure is logged and, for a check nobody has answered, the row is
    /// marked Expired with the error in ExpiredReason. An ANSWERED check is never expired
    /// for a sweep failure: that would overwrite the reporter's verdict with "nobody
    /// replied" and move a real answer out of the confirmation rate. It is logged and left
    /// for the next pass.
    ///
    /// Never throws for a single row; cancellation and a database that cannot even be
    /// queried still propagate, and the hosted service catches those per pass.
    ///
    /// The body lives here, in a scoped service, rather than inside the hosted service that
    /// calls it on a timer — the same split as WorkflowRunner and IWorkflowService. That
    /// keeps the rule testable without starting a background worker, and keeps a scoped
    /// DbContext out of a singleton. POST /api/verifications/run-sweep calls it too; a
    /// static lock stops that and the timer running at once.
    ///
    /// Deliberately does no asking of its own: what "notify the reporter" means is a
    /// delivery concern this project does not have yet, so the state change IS the
    /// notification — the check appears on the reporter's list.
    /// </summary>
    Task<VerificationSweepResultDto> ProcessDueChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the reporter's verdict and moves the check to Confirmed or Reopened in the
    /// same SaveChanges, so the two can never disagree — the same rule, and the same
    /// reason, as ClarificationService moving a report to AwaitingClarification alongside
    /// its questions.
    ///
    /// Returns false when the check does not exist OR has already been answered. A caller
    /// that needs to tell those apart calls <see cref="GetByIdAsync"/> first: a check with
    /// a non-null ReporterRespondedAt has been answered, and re-answering it is a 409.
    ///
    /// ONE ANSWER, NOT A THREAD. A check that has been answered is finished; there is no
    /// second round, and nothing replies to the comment.
    /// </summary>
    Task<bool> RecordReporterResponseAsync(
        int id,
        ReporterConfirmationDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How well repairs are actually holding, counted across every check. See MetricsDto —
    /// every figure is computed here in C#, never by a model.
    /// </summary>
    Task<MetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The coarse filter the checks list offers. Deliberately not the full VerificationStatus
/// enum: a worklist is read as "what needs doing" or "what is finished", and offering six
/// separate filters would make the caller reconstruct that grouping itself.
/// </summary>
public enum VerificationStatusFilter
{
    All,

    /// <summary>Pending and AwaitingReporterResponse — still in flight.</summary>
    Open,

    /// <summary>Confirmed, Reopened, Escalated and Expired — answered or given up on.</summary>
    Closed,

    /// <summary>DueAt has passed and the sweep has not moved it on. See MetricsDto.</summary>
    Overdue
}
