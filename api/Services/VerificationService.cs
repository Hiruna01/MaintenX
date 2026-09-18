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

    private readonly AppDbContext _db;
    private readonly VerificationSettings _settings;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        AppDbContext db,
        VerificationSettings settings,
        ILogger<VerificationService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

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
        var now = DateTime.UtcNow;

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

    public async Task<int> ProcessDueChecksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Exactly the shape the composite index on (Status, DueAt) serves: equality on the
        // leading column, range on the second.
        var due = await _db.VerificationChecks
            .Where(v => v.Status == VerificationStatus.Pending && v.DueAt <= now)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var check in due)
        {
            check.Status = VerificationStatus.AwaitingReporterResponse;
            check.ProcessedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Verification sweep moved {Count} check(s) to AwaitingReporterResponse.", due.Count);

        return due.Count;
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
        check.ReporterRespondedAt = DateTime.UtcNow;

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
        var now = DateTime.UtcNow;

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
