using System.Text.Json;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class VerificationService : IVerificationService
{
    /// <summary>
    /// The states a check can still move out of. Kept as an array rather than a helper
    /// method because EF translates Contains into a SQL IN and cannot translate a method
    /// call — and with the enum stored as a string that IN reads as
    /// ('Pending','AwaitingReporterResponse') in the query log.
    /// </summary>
    private static readonly VerificationStatus[] OpenStatuses =
    {
        VerificationStatus.Pending,
        VerificationStatus.AwaitingReporterResponse
    };

    /// <summary>
    /// One sweep at a time. The timer and POST /api/workflows/verification-sweep can both start
    /// one, and two passes over the same rows would each count the same check as asked.
    /// Static because the service is scoped: every instance must share the one lock — same
    /// as GoogleCalendarSyncService.
    /// </summary>
    private static readonly SemaphoreSlim SweepLock = new(1, 1);

    /// <summary>Column limit on VerificationCheck.ExpiredReason, so a long error is cut rather than refused.</summary>
    private const int MaxExpiredReasonLength = 500;

    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    private readonly VerificationSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        AppDbContext db,
        VerificationSettings settings,
        TimeProvider time,
        ILogger<VerificationService> logger)
    {
        _db = db;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// "Now" from the injected clock, never DateTime.UtcNow — the response window is a
    /// date rule, and a test has to be able to stand on either side of it.
    /// </summary>
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<VerificationCheckDto?> CreateForCompletedWorkOrderAsync(
        int workOrderId,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await _db.WorkOrders
            .Include(w => w.Asset)
            .Include(w => w.Report)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            return null;
        }

        // Only a finished repair can be verified. Raising a check against work still in
        // progress would ask the reporter about something nobody has claimed to have done.
        if (workOrder.Status != WorkOrderStatus.Completed || workOrder.CompletedAt is null)
        {
            _logger.LogWarning(
                "Cannot verify work order {WorkOrderId}: status is {Status} with CompletedAt {CompletedAt}.",
                workOrderId,
                workOrder.Status,
                workOrder.CompletedAt);
            return null;
        }

        if (await HasOpenCheckAsync(workOrderId, cancellationToken))
        {
            return null;
        }

        // THE DELAY IS THE POINT. Measured from completion rather than from now, so a
        // check raised late by a backfill still falls due when it should have.
        var check = new VerificationCheck
        {
            WorkOrderId = workOrder.Id,
            AssetId = workOrder.AssetId,
            DueAt = workOrder.CompletedAt.Value.AddDays(_settings.DelayDays),
            Status = VerificationStatus.Pending
        };

        _db.VerificationChecks.Add(check);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(check, workOrder.Asset?.AssetTag ?? string.Empty, workOrder.ReportId,
            workOrder.Report?.Description ?? string.Empty, workOrder.CompletedAt, IsOverdue(check));
    }

    public Task<bool> HasOpenCheckAsync(int workOrderId, CancellationToken cancellationToken = default) =>
        _db.VerificationChecks.AnyAsync(
            v => v.WorkOrderId == workOrderId && OpenStatuses.Contains(v.Status),
            cancellationToken);

    /// <summary>
    /// THE VISIBILITY RULE, in one place so it cannot be applied to the list and forgotten
    /// on the detail read. A FacilitiesManager and an Admin see every check; everybody else
    /// sees the checks on faults they reported.
    ///
    /// Written as "who sees everything", so it FAILS CLOSED — the same shape as
    /// ReportService.SeesEveryReport, and a check is a question about a report, so the two
    /// rules have to agree. A Technician is scoped today: the check is the reporter's
    /// verdict on the technician's work, not the technician's worklist.
    /// </summary>
    private static bool SeesEveryCheck(Role role) =>
        role is Role.FacilitiesManager or Role.Admin;

    public async Task<PagedResult<VerificationCheckDto>> GetAllAsync(
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
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject, the same as every other list here.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.VerificationChecks.AsNoTracking();

        // THE VISIBILITY SCOPE GOES ON FIRST, before the filters and the count. The
        // reporter is reached through the work order to its report — the check carries no
        // reporter of its own, so there is no second copy of "who filed this" to drift.
        if (!SeesEveryCheck(callerRole))
        {
            query = query.Where(v => v.WorkOrder!.Report!.ReporterId == callerId);
        }

        // The asset tag — the sticker on the machine, and the one text column a check row
        // shows. ToLower().Contains(), never EF.Functions.ILike: ILike is Npgsql-only, and
        // lowering both sides is what makes SQLite and PostgreSQL agree.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(v => v.Asset!.AssetTag.ToLower().Contains(term));
        }

        if (status is not null)
        {
            query = query.Where(v => v.Status == status);
        }

        if (assetId is not null)
        {
            query = query.Where(v => v.AssetId == assetId);
        }

        // Calendar dates turned into UTC bounds here, in C#, so only DateTime values reach
        // the query — DateOnly arithmetic does not translate to SQLite. dateTo is made
        // inclusive by comparing against the start of the day AFTER it.
        if (dateFrom is not null)
        {
            var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(v => v.DueAt >= from);
        }

        if (dateTo is not null)
        {
            var toExclusive = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(v => v.DueAt < toExclusive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Id breaks every tie. Checks share a DueAt readily — completions batched into one
        // SaveChanges get the same delay added to the same instant — and without a total
        // order a row could appear on two pages or on neither.
        query = sort switch
        {
            VerificationSort.Status => query
                .OrderBy(v => v.Status)
                .ThenByDescending(v => v.DueAt)
                .ThenByDescending(v => v.Id),
            _ => query
                .OrderByDescending(v => v.DueAt)
                .ThenByDescending(v => v.Id)
        };

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                Check = v,
                AssetTag = v.Asset!.AssetTag,
                v.WorkOrder!.ReportId,
                ReportDescription = v.WorkOrder.Report!.Description,
                v.WorkOrder.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<VerificationCheckDto>(
            rows.Select(r => ToDto(r.Check, r.AssetTag, r.ReportId, r.ReportDescription, r.CompletedAt, IsOverdue(r.Check))).ToList(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<VerificationDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default)
    {
        var check = await _db.VerificationChecks
            .AsNoTracking()
            .Include(v => v.Asset)
            .Include(v => v.WorkOrder)
                .ThenInclude(w => w!.Report)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (check?.Asset is null || check.WorkOrder?.Report is null)
        {
            return null;
        }

        var seesEverything = SeesEveryCheck(callerRole);

        if (!seesEverything && check.WorkOrder.Report.ReporterId != callerId)
        {
            return null;
        }

        // What has happened to this machine since the repair — other people's reports and
        // work orders, so a manager's view only (see VerificationDetailDto). Measured from
        // CompletedAt, the moment the repair was claimed; a check on an order with no
        // completion time has no "since", and that is null too rather than a guess.
        IReadOnlyList<VerificationRelatedReportDto>? newReports = null;
        IReadOnlyList<VerificationFollowUpDto>? followUps = null;

        if (seesEverything && check.WorkOrder.CompletedAt is { } completedAt)
        {
            var originalReportId = check.WorkOrder.ReportId;

            newReports = await _db.Reports
                .AsNoTracking()
                .Where(r => r.AssetId == check.AssetId
                            && r.Id != originalReportId
                            && r.CreatedAt > completedAt)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .Select(r => new VerificationRelatedReportDto(r.Id, r.Description, r.Status, r.CreatedAt))
                .ToListAsync(cancellationToken);

            followUps = await _db.WorkOrders
                .AsNoTracking()
                .Where(w => w.AssetId == check.AssetId
                            && w.Id != check.WorkOrderId
                            && w.CreatedAt > completedAt)
                .OrderBy(w => w.CreatedAt)
                .ThenBy(w => w.Id)
                .Select(w => new VerificationFollowUpDto(w.Id, w.Status, w.Strategy, w.CreatedAt))
                .ToListAsync(cancellationToken);
        }

        return new VerificationDetailDto(
            check.Id,
            check.WorkOrderId,
            check.WorkOrder.ReportId,
            check.WorkOrder.Report.Description,
            ToAssetDto(check.Asset),
            check.WorkOrder.ResolutionNote,
            check.WorkOrder.CompletedAt,
            check.DueAt,
            check.Status,
            IsOverdue(check),
            check.ReporterConfirmed,
            check.ReporterComment,
            check.ReporterRespondedAt,
            check.AgentOutcome,
            check.AgentReason,
            ReadEvidence(check.AgentEvidenceJson),
            newReports,
            followUps,
            check.ProcessedAt,
            check.AgentQueuedAt,
            check.ExpiredReason,
            check.CreatedAt,
            check.UpdatedAt);
    }

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.VerificationChecks.AnyAsync(v => v.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VerificationCheckDto>> GetForWorkOrderAsync(
        int workOrderId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.WorkOrderId == workOrderId)
            // Oldest first here, unlike the list: read against one work order these are a
            // sequence — the first repair, then whatever followed it being reopened — and
            // that only reads as a sequence in the order it happened.
            .OrderBy(v => v.Id)
            .Select(v => new
            {
                Check = v,
                AssetTag = v.Asset!.AssetTag,
                v.WorkOrder!.ReportId,
                ReportDescription = v.WorkOrder.Report!.Description,
                v.WorkOrder.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => ToDto(r.Check, r.AssetTag, r.ReportId, r.ReportDescription, r.CompletedAt, IsOverdue(r.Check))).ToList();
    }

    public async Task<VerificationSweepResultDto> ProcessDueChecksAsync(
        CancellationToken cancellationToken = default)
    {
        await SweepLock.WaitAsync(cancellationToken);

        try
        {
            return await SweepLockedAsync(cancellationToken);
        }
        finally
        {
            SweepLock.Release();
        }
    }

    private async Task<VerificationSweepResultDto> SweepLockedAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var asked = 0;
        var queued = 0;
        var failed = 0;

        // STEP 0 — the workflows. Completed ones whose repair is VerificationSettings.DelayDays
        // old move to AwaitingVerification. The same delay, measured from the same instant
        // (WorkOrderService stamps the workflow and the order with one CompletedAt), as the
        // check below falling due, so the workflow and the check reach "ask" in the same pass.
        var (movedWorkflows, failedWorkflows) = await AdvanceCompletedWorkflowsAsync(now, cancellationToken);
        failed += failedWorkflows;

        // STEP 1 — ask the reporter. Exactly the shape the composite index on
        // (Status, DueAt) serves: equality on the leading column, range on the second.
        // Ids only: each row is loaded, changed and saved on its own below.
        var dueIds = await _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.Status == VerificationStatus.Pending && v.DueAt <= now)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in dueIds)
        {
            var outcome = await RunStepAsync(id, "asking the reporter", check =>
            {
                // Re-read, not assumed: the row may have moved since the ids were listed.
                if (check.Status != VerificationStatus.Pending)
                {
                    return false;
                }

                check.Status = VerificationStatus.AwaitingReporterResponse;
                check.ProcessedAt = now;
                return true;
            }, now, cancellationToken);

            if (outcome == StepOutcome.Done) asked++;
            if (outcome == StepOutcome.Failed) failed++;
        }

        // STEP 2 — hand to the verification agent. Answered, or asked and silent past the
        // response window. Measured from ProcessedAt, when the reporter was actually asked;
        // DueAt covers seeded rows the sweep never stamped, the same fallback the metrics
        // use. A check asked in step 1 has ProcessedAt = now, so it cannot qualify here.
        var silentSince = now.AddDays(-_settings.ResponseWindowDays);

        var readyIds = await _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.AgentQueuedAt == null
                && (v.ReporterRespondedAt != null
                    || (v.Status == VerificationStatus.AwaitingReporterResponse
                        && (v.ProcessedAt ?? v.DueAt) <= silentSince)))
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in readyIds)
        {
            var outcome = await RunStepAsync(id, "queueing it for the verification agent", check =>
            {
                if (check.AgentQueuedAt is not null)
                {
                    return false;
                }

                // Status is NOT touched. Queueing is not a verdict: an answered check keeps
                // the status its answer gave it, and a silent one stays open for the
                // reporter to answer late. The agent writes AgentOutcome; C# sets Status.
                check.AgentQueuedAt = now;
                return true;
            }, now, cancellationToken);

            if (outcome == StepOutcome.Done) queued++;
            if (outcome == StepOutcome.Failed) failed++;
        }

        var processed = movedWorkflows + asked + queued + failed;

        if (processed > 0)
        {
            _logger.LogInformation(
                "Verification sweep: {Workflows} workflow(s) now awaiting verification, {Asked} asked the "
                + "reporter, {Queued} queued for the agent, {Failed} failed.",
                movedWorkflows, asked, queued, failed);
        }

        return new VerificationSweepResultDto(processed, movedWorkflows, asked, queued, failed);
    }

    /// <summary>
    /// Step 0 of the sweep: Completed workflows whose CompletedAt is at least DelayDays old,
    /// moved to AwaitingVerification through WorkflowTransitions.
    ///
    /// IDEMPOTENT BY STATE: a moved workflow is no longer Completed, so no later pass can pick
    /// it again, and nothing here creates a row — the verification check it waits on was raised
    /// with the completion itself. Running the sweep twice moves each workflow once.
    ///
    /// One workflow at a time, each saved on its own, like the check steps. A workflow that
    /// throws is logged and left Completed for the next pass — there is no answer on it to
    /// protect and no Expired state to give it; a workflow is only ever moved forward.
    /// </summary>
    private async Task<(int Moved, int Failed)> AdvanceCompletedWorkflowsAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var completedBy = now.AddDays(-_settings.DelayDays);

        var dueIds = await _db.AgentWorkflows
            .AsNoTracking()
            .Where(w => w.CurrentState == WorkflowState.Completed
                && w.CompletedAt != null
                && w.CompletedAt <= completedBy)
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var moved = 0;
        var failed = 0;

        foreach (var id in dueIds)
        {
            try
            {
                var workflow = await _db.AgentWorkflows.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

                // Re-read, not assumed: the row may have moved since the ids were listed.
                if (workflow is null || workflow.CurrentState != WorkflowState.Completed)
                {
                    continue;
                }

                WorkflowTransitions.Move(workflow, WorkflowTrigger.VerificationDue);
                workflow.Outcome = "The repair is being verified with the reporter.";

                await _db.SaveChangesAsync(cancellationToken);
                moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Verification sweep failed on workflow {WorkflowId}.", id);

                // Same reason as RunStepAsync: the failed change would ride along with the next save.
                _db.ChangeTracker.Clear();
                failed++;
            }
        }

        return (moved, failed);
    }

    private enum StepOutcome
    {
        Done,

        /// <summary>Gone, or already moved on by the time it was loaded. Not counted.</summary>
        Skipped,

        Failed
    }

    /// <summary>
    /// Loads one check, applies one change and saves it on its own. THE PER-ROW CATCH:
    /// whatever this row throws is logged and handed to <see cref="ExpireAfterFailureAsync"/>,
    /// and the sweep moves on to the next row. Only cancellation escapes.
    /// </summary>
    private async Task<StepOutcome> RunStepAsync(
        int id,
        string step,
        Func<VerificationCheck, bool> apply,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            var check = await _db.VerificationChecks.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

            if (check is null || !apply(check))
            {
                return StepOutcome.Skipped;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return StepOutcome.Done;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Verification sweep failed on check {CheckId} while {Step}.", id, step);

            // The failed change is still tracked, and the next SaveChanges would try it
            // again — failing every row after this one for this row's fault.
            _db.ChangeTracker.Clear();

            await ExpireAfterFailureAsync(id, step, ex, now, cancellationToken);
            return StepOutcome.Failed;
        }
    }

    /// <summary>
    /// Gives up on a check the sweep could not process: Expired, with the error in
    /// ExpiredReason so the row says why rather than just stopping. Expired rather than
    /// left in place because a row that fails every pass would fail every pass forever,
    /// logging the same error hourly while the reporter is never asked.
    ///
    /// NOT for an answered check. Expired means "asked, never answered"; writing it over a
    /// reporter's verdict would destroy the answer and pull it out of the confirmation
    /// rate. Those are logged and left for the next pass.
    ///
    /// Never throws: if even this write fails, it is logged and the sweep carries on.
    /// </summary>
    private async Task ExpireAfterFailureAsync(
        int id,
        string step,
        Exception failure,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            var check = await _db.VerificationChecks.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

            if (check is null)
            {
                return;
            }

            if (check.ReporterRespondedAt is not null)
            {
                _logger.LogWarning(
                    "Verification check {CheckId} has been answered, so it is left as {Status} "
                    + "for the next sweep rather than expired.",
                    id, check.Status);
                return;
            }

            var reason = $"The verification sweep failed while {step}: {failure.GetBaseException().Message}";

            check.Status = VerificationStatus.Expired;
            check.ExpiredReason = reason.Length <= MaxExpiredReasonLength
                ? reason
                : reason[..MaxExpiredReasonLength];
            check.ProcessedAt = now;

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not mark verification check {CheckId} Expired after the sweep failed on it.", id);
            _db.ChangeTracker.Clear();
        }
    }

    public async Task<ConfirmVerificationOutcome> RecordReporterResponseAsync(
        int id,
        int callerId,
        ReporterConfirmationDto dto,
        CancellationToken cancellationToken = default)
    {
        var check = await _db.VerificationChecks
            .Include(v => v.WorkOrder)
                .ThenInclude(w => w!.Report)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (check is null)
        {
            return ConfirmVerificationOutcome.NotFound;
        }

        // IDENTITY BEFORE STATE. The question was put to whoever filed the report, and
        // nobody else's opinion of the repair is being asked for — a manager and an Admin
        // included, who could otherwise close the loop on the reporter's behalf.
        if (check.WorkOrder?.Report?.ReporterId != callerId)
        {
            return ConfirmVerificationOutcome.NotTheReporter;
        }

        // ONE ANSWER PER CHECK, and this is where that is enforced. There is no unique
        // index to lean on here — the answer lives in columns on this row rather than in a
        // second table — so a service that means "reject a re-answer" has to look and say
        // so. The same lesson as ClarificationAnswer, arrived at from the other direction.
        if (check.ReporterRespondedAt is not null)
        {
            return ConfirmVerificationOutcome.AlreadyAnswered;
        }

        if (check.Status != VerificationStatus.AwaitingReporterResponse)
        {
            return ConfirmVerificationOutcome.NotAwaitingResponse;
        }

        var now = UtcNow;

        check.ReporterConfirmed = dto.Confirmed!.Value;
        check.ReporterComment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        check.ReporterRespondedAt = now;

        // WHAT THE ANSWER MEANS IS A DETERMINISTIC RULE AND LIVES HERE, not in a prompt.
        // Set in the same SaveChanges as the answer itself, so the two can never disagree:
        // a check is never left Confirmed with ReporterConfirmed false, nor answered with
        // a status that still says nobody has replied.
        //
        // Reopening does not raise the follow-up work order — that belongs to Component C,
        // and this row is the record that the first repair did not hold.
        check.Status = dto.Confirmed.Value
            ? VerificationStatus.Confirmed
            : VerificationStatus.Reopened;

        // Queued for the agent now, with the answer, rather than on the next sweep. A
        // check already queued as SILENT (asked, no reply past the window, then answered
        // late) is stamped again: what the agent is handed has changed from silence to an
        // answer, and the stamp says when.
        check.AgentQueuedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return ConfirmVerificationOutcome.Success;
    }

    public async Task<VerificationMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow;

        // One grouped query rather than six counts, so the whole picture comes from a
        // single trip and cannot be assembled from rows that changed in between.
        var counts = await _db.VerificationChecks
            .AsNoTracking()
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        int CountOf(VerificationStatus status) => counts.TryGetValue(status, out var c) ? c : 0;

        var pending = CountOf(VerificationStatus.Pending);
        var awaiting = CountOf(VerificationStatus.AwaitingReporterResponse);
        var confirmed = CountOf(VerificationStatus.Confirmed);
        var reopened = CountOf(VerificationStatus.Reopened);
        var escalated = CountOf(VerificationStatus.Escalated);
        var expired = CountOf(VerificationStatus.Expired);

        // ANSWERED checks only. Expired ones are excluded on purpose: nobody replied to
        // them, and folding silence into either column would report a result that was
        // never given. See VerificationMetricsDto.
        var answered = confirmed + reopened;

        // The same Percent — and the same denominator — as the reopen rate in
        // AnalyticsService, so the two pages cannot show two different figures.
        var confirmationRate = MetricRules.Percent(confirmed, answered);
        var reopenRate = MetricRules.Percent(reopened, answered);

        // Only the two columns the average needs, and the subtraction itself happens in
        // C#. DateTime arithmetic translated into SQL behaves differently on PostgreSQL
        // and on the SQLite the tests use, and this figure is small enough that there is
        // nothing to gain by pushing it down.
        var responded = await _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.ReporterRespondedAt != null)
            .Select(v => new { v.ReporterRespondedAt, v.ProcessedAt, v.DueAt })
            .ToListAsync(cancellationToken);

        double? averageDaysToRespond = responded.Count == 0
            ? null
            // Measured from when the reporter was actually ASKED — ProcessedAt — because
            // this is a number about reporters. Falling back to DueAt covers rows the
            // sweep never stamped, such as seeded history.
            : Math.Round(
                responded.Average(r => (r.ReporterRespondedAt!.Value - (r.ProcessedAt ?? r.DueAt)).TotalDays),
                2);

        var overdueUnprocessed = await _db.VerificationChecks
            .CountAsync(v => v.Status == VerificationStatus.Pending && v.DueAt <= now, cancellationToken);

        return new VerificationMetricsDto(
            Total: pending + awaiting + confirmed + reopened + escalated + expired,
            Pending: pending,
            AwaitingReporterResponse: awaiting,
            Confirmed: confirmed,
            Reopened: reopened,
            Escalated: escalated,
            Expired: expired,
            ConfirmationRate: confirmationRate,
            ReopenRate: reopenRate,
            AverageDaysToRespond: averageDaysToRespond,
            OverdueUnprocessed: overdueUnprocessed);
    }

    /// <summary>
    /// Whether a check has missed the deadline its current state is waiting on. The same two
    /// clocks the sweep runs on, read from the same settings:
    ///
    ///   * Pending and DueAt passed — due to be asked, and the sweep has not asked yet. This
    ///     is exactly what VerificationMetricsDto.OverdueUnprocessed counts.
    ///   * AwaitingReporterResponse for longer than ResponseWindowDays since the reporter was
    ///     asked (ProcessedAt, falling back to DueAt for seeded rows) — the sweep's own
    ///     "silent" test.
    ///
    /// A closed check is never overdue: there is nothing left to wait for.
    /// </summary>
    private bool IsOverdue(VerificationCheck v)
    {
        var now = UtcNow;

        return v.Status switch
        {
            VerificationStatus.Pending => v.DueAt <= now,
            VerificationStatus.AwaitingReporterResponse =>
                (v.ProcessedAt ?? v.DueAt) <= now.AddDays(-_settings.ResponseWindowDays),
            _ => false
        };
    }

    /// <summary>
    /// The agent's evidence array, or null when there is none. Read defensively — it is what
    /// a model produced — so a value that is not an array of strings is null, never thrown
    /// on, and a non-string item is skipped.
    /// </summary>
    private static IReadOnlyList<string>? ReadEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static VerificationCheckDto ToDto(
        VerificationCheck v,
        string assetTag,
        int reportId,
        string reportDescription,
        DateTime? completedAt,
        bool isOverdue) =>
        new(v.Id,
            v.WorkOrderId,
            reportId,
            reportDescription,
            completedAt,
            v.AssetId,
            assetTag,
            v.DueAt,
            v.Status,
            v.ReporterConfirmed,
            isOverdue,
            v.ReporterRespondedAt,
            v.CreatedAt,
            v.UpdatedAt);

    private static AssetDto ToAssetDto(Asset a) =>
        new(a.Id,
            a.AssetTag,
            a.Name,
            a.AssetCategoryId,
            a.RoomId,
            a.Manufacturer,
            a.Model,
            a.InstalledOn,
            a.WarrantyExpiresOn,
            a.Status,
            a.CreatedAt,
            a.UpdatedAt);
}
