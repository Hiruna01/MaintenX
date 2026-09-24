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
    /// One sweep at a time. The timer and POST /api/verifications/run-sweep can both start
    /// one, and two passes over the same rows would each count the same check as asked.
    /// Static because the service is scoped: every instance must share the one lock — same
    /// as GoogleCalendarSyncService.
    /// </summary>
    private static readonly SemaphoreSlim SweepLock = new(1, 1);

    /// <summary>Column limit on VerificationCheck.ExpiredReason, so a long error is cut rather than refused.</summary>
    private const int MaxExpiredReasonLength = 500;

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

        return ToDto(check, workOrder.Asset?.AssetTag ?? string.Empty);
    }

    public Task<bool> HasOpenCheckAsync(int workOrderId, CancellationToken cancellationToken = default) =>
        _db.VerificationChecks.AnyAsync(
            v => v.WorkOrderId == workOrderId && OpenStatuses.Contains(v.Status),
            cancellationToken);

    public async Task<IReadOnlyList<VerificationCheckDto>> GetAllAsync(
        VerificationStatusFilter filter = VerificationStatusFilter.All,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow;

        var query = _db.VerificationChecks.AsNoTracking();

        query = filter switch
        {
            VerificationStatusFilter.Open => query.Where(v => OpenStatuses.Contains(v.Status)),
            VerificationStatusFilter.Closed => query.Where(v => !OpenStatuses.Contains(v.Status)),
            // The query the composite index on (Status, DueAt) exists for.
            VerificationStatusFilter.Overdue => query.Where(
                v => v.Status == VerificationStatus.Pending && v.DueAt <= now),
            _ => query
        };

        var rows = await query
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .Select(v => new { Check = v, AssetTag = v.Asset!.AssetTag })
            .ToListAsync(cancellationToken);

        return rows.Select(r => ToDto(r.Check, r.AssetTag)).ToList();
    }

    public async Task<VerificationDetailDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var check = await _db.VerificationChecks
            .AsNoTracking()
            .Include(v => v.Asset)
            .Include(v => v.WorkOrder)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (check?.Asset is null)
        {
            return null;
        }

        return new VerificationDetailDto(
            check.Id,
            check.WorkOrderId,
            ToAssetDto(check.Asset),
            check.WorkOrder?.ResolutionNote,
            check.WorkOrder?.CompletedAt,
            check.DueAt,
            check.Status,
            check.ReporterConfirmed,
            check.ReporterComment,
            check.ReporterRespondedAt,
            check.AgentOutcome,
            check.AgentReason,
            check.ProcessedAt,
            check.AgentQueuedAt,
            check.ExpiredReason,
            check.CreatedAt,
            check.UpdatedAt);
    }

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
            .Select(v => new { Check = v, AssetTag = v.Asset!.AssetTag })
            .ToListAsync(cancellationToken);

        return rows.Select(r => ToDto(r.Check, r.AssetTag)).ToList();
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

        var processed = asked + queued + failed;

        if (processed > 0)
        {
            _logger.LogInformation(
                "Verification sweep: {Asked} asked the reporter, {Queued} queued for the agent, {Failed} failed.",
                asked, queued, failed);
        }

        return new VerificationSweepResultDto(processed, asked, queued, failed);
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

    public async Task<bool> RecordReporterResponseAsync(
        int id,
        ReporterConfirmationDto dto,
        CancellationToken cancellationToken = default)
    {
        var check = await _db.VerificationChecks
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (check is null)
        {
            return false;
        }

        // ONE ANSWER PER CHECK, and this is where that is enforced. There is no unique
        // index to lean on here — the answer lives in columns on this row rather than in a
        // second table — so a service that means "reject a re-answer" has to look and say
        // so. The same lesson as ClarificationAnswer, arrived at from the other direction.
        if (check.ReporterRespondedAt is not null || !OpenStatuses.Contains(check.Status))
        {
            _logger.LogWarning(
                "Verification check {CheckId} has already been answered or closed (status {Status}).",
                id,
                check.Status);
            return false;
        }

        check.ReporterConfirmed = dto.Confirmed;
        check.ReporterComment = dto.Comment;
        check.ReporterRespondedAt = UtcNow;

        // WHAT THE ANSWER MEANS IS A DETERMINISTIC RULE AND LIVES HERE, not in a prompt.
        // Set in the same SaveChanges as the answer itself, so the two can never disagree:
        // a check is never left Confirmed with ReporterConfirmed false, nor answered with
        // a status that still says nobody has replied.
        //
        // Reopening does not raise the follow-up work order — that belongs to Component C,
        // and this row is the record that the first repair did not hold.
        check.Status = dto.Confirmed
            ? VerificationStatus.Confirmed
            : VerificationStatus.Reopened;

        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<MetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
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
        // never given. See MetricsDto.
        var answered = confirmed + reopened;

        var confirmationRate = answered == 0
            ? 0m
            : Math.Round(confirmed * 100m / answered, 2);

        var reopenRate = answered == 0
            ? 0m
            : Math.Round(reopened * 100m / answered, 2);

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

        return new MetricsDto(
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

    private static VerificationCheckDto ToDto(VerificationCheck v, string assetTag) =>
        new(v.Id,
            v.WorkOrderId,
            v.AssetId,
            assetTag,
            v.DueAt,
            v.Status,
            v.ReporterConfirmed,
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
