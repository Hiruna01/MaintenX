using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

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
    /// One page of checks, filtered and sorted, through the existing PagedResult&lt;T&gt;.
    /// There is no second pagination type in this project.
    ///
    /// WHO MAY SEE WHAT IS DECIDED HERE, NOT BY THE CALLER — the same rule, written the
    /// same way, as IReportService.GetAllAsync. A FacilitiesManager and an Admin see every
    /// check; anyone else sees the checks on repairs to faults THEY reported, reached
    /// through the check's work order to its report. The scope is a Where applied before
    /// the count and paging, and no parameter widens it.
    ///
    /// <paramref name="status"/>, <paramref name="assetId"/> are exact filters.
    /// <paramref name="dateFrom"/> and <paramref name="dateTo"/> bound DueAt — when the
    /// question falls due, the one date every check has — as UTC calendar dates, BOTH ENDS
    /// INCLUSIVE, the same arithmetic as the report list. <paramref name="search"/> matches
    /// the asset tag, case-insensitively, inside the same scope.
    /// </summary>
    Task<PagedResult<VerificationCheckDto>> GetAllAsync(
        int callerId,
        Role callerRole,
        string? search = null,
        VerificationStatus? status = null,
        int? assetId = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        VerificationSort sort = VerificationSort.DueAt,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One check with its asset and the work order claim it is testing, under the same
    /// visibility rule as <see cref="GetAllAsync"/> — a list that hid other people's checks
    /// while a read by id handed them over would be a rule that only looks enforced.
    ///
    /// Null both when no check has that id and when the caller may not see it. The
    /// controller tells those apart with <see cref="ExistsAsync"/>, the same way
    /// ReportsController does.
    /// </summary>
    Task<VerificationDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a check exists AT ALL, ignoring who is asking. Used to tell a 404 from a 403
    /// after <see cref="GetDetailAsync"/> returns null.
    /// </summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

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
    /// Records the reporter's answer to "Is the problem fixed?" and moves the check to
    /// Confirmed or Reopened in the same SaveChanges, so the two can never disagree — the
    /// same rule, and the same reason, as ClarificationService moving a report to
    /// AwaitingClarification alongside its questions.
    ///
    /// Every check is C#, in a fixed order — identity before state, so a stranger learns
    /// nothing about where somebody else's check has got to:
    ///
    ///   1. no such check                                          → NotFound
    ///   2. the caller did not file the report the repair was for  → NotTheReporter
    ///   3. already answered                                       → AlreadyAnswered
    ///   4. not AwaitingReporterResponse                           → NotAwaitingResponse
    ///
    /// Answered is looked at before the status because an answered check is Confirmed or
    /// Reopened by then, and "not awaiting a response" would be true but less useful. Both
    /// are 409s; the order only chooses the message.
    ///
    /// Only AwaitingReporterResponse may be answered — not Pending. A check still waiting
    /// out its delay has not been put to anyone yet, and an answer given before the delay
    /// has passed is exactly the same-afternoon "yes" the delay exists to avoid.
    ///
    /// On success the check is also QUEUED FOR THE AGENT (AgentQueuedAt) in that same
    /// SaveChanges, rather than left for the next sweep to notice. The sweep only queues
    /// rows whose AgentQueuedAt is null, so it will not queue this one a second time.
    ///
    /// ONE ANSWER, NOT A THREAD. A check that has been answered is finished; there is no
    /// second round, and nothing replies to the comment.
    /// </summary>
    Task<ConfirmVerificationOutcome> RecordReporterResponseAsync(
        int id,
        int callerId,
        ReporterConfirmationDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How well repairs are actually holding, counted across every check. See
    /// VerificationMetricsDto — every figure is computed here in C#, never by a model.
    /// </summary>
    Task<VerificationMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What <see cref="IVerificationService.RecordReporterResponseAsync"/> made of an answer.
/// There is no Result wrapper in this project; the controller maps each member to a status
/// code, the same shape as SubmitAnswersOutcome.
/// </summary>
public enum ConfirmVerificationOutcome
{
    /// <summary>Answer recorded, status set from it, check queued for the agent. A 204.</summary>
    Success,

    /// <summary>No check has that id. A 404.</summary>
    NotFound,

    /// <summary>The caller is not the reporter of the report the repair was for. A 403.</summary>
    NotTheReporter,

    /// <summary>The check has an answer already, and the first one stands. A 409.</summary>
    AlreadyAnswered,

    /// <summary>
    /// The check is not waiting on the reporter — still in its delay, or closed without an
    /// answer (Escalated, Expired). A 409.
    /// </summary>
    NotAwaitingResponse
}
