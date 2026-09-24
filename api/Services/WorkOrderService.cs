using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class WorkOrderService : IWorkOrderService
{
    /// <summary>Largest page a client may ask for, so one request cannot pull the table.</summary>
    private const int MaxPageSize = 100;

    /// <summary>
    /// The statuses of an order that has cleared approval and is not yet finished — the only
    /// ones that can be assigned or completed. Draft and AwaitingApproval have not been
    /// cleared to spend money; Rejected, Completed and Cancelled are over.
    /// </summary>
    private static readonly WorkOrderStatus[] ActiveStatuses =
    {
        WorkOrderStatus.Approved,
        WorkOrderStatus.Scheduled,
        WorkOrderStatus.InProgress
    };

    private readonly AppDbContext _db;
    private readonly ApprovalSettings _approval;
    private readonly IWorkflowQueue _workflowQueue;
    private readonly IVerificationService _verificationService;
    private readonly TimeProvider _time;
    private readonly SchedulingSettings _scheduling;

    public WorkOrderService(
        AppDbContext db,
        ApprovalSettings approval,
        IWorkflowQueue workflowQueue,
        IVerificationService verificationService,
        TimeProvider time,
        SchedulingSettings scheduling)
    {
        _db = db;
        _approval = approval;
        _workflowQueue = workflowQueue;
        _verificationService = verificationService;
        _time = time;
        _scheduling = scheduling;
    }

    /// <summary>
    /// The longest date range one availability search covers, inclusive. Twenty slots are
    /// usually found in the first day or two; the cap stops a mistyped year from loading a
    /// year of timetable to find them.
    /// </summary>
    private const int MaxSlotSearchDays = 31;

    /// <summary>
    /// THE VISIBILITY RULE, written as "who sees everything" so it FAILS CLOSED — the same
    /// shape as ReportService.SeesEveryReport. Everybody else is scoped to the orders
    /// assigned to them: a Technician's queue, and nothing at all for a Reporter, whose
    /// view of the fault is the report. A role added to the enum later sees nothing until
    /// somebody deliberately widens this.
    /// </summary>
    private static bool SeesEveryWorkOrder(Role role) =>
        role is Role.FacilitiesManager or Role.Admin;

    /// <summary>
    /// THE APPROVAL GATE. Decimal against decimal, in C# — see ApprovalSettings for why it
    /// is never SQL and never a prompt.
    ///
    /// Strictly ABOVE the threshold: an estimate sitting exactly on it does not need a
    /// manager. EscalateReplacement always does, whatever it costs — replacing equipment
    /// is a decision about the estate, not just about the money, and a cheap replacement
    /// is still a replacement.
    /// </summary>
    private bool RequiresApproval(decimal estimatedCost, WorkOrderStrategy strategy) =>
        estimatedCost > _approval.CostThreshold
        || strategy == WorkOrderStrategy.EscalateReplacement;

    public async Task<PagedResult<WorkOrderDto>> GetAllAsync(
        int callerId,
        Role callerRole,
        WorkOrderStatus? status = null,
        int? technicianId = null,
        int? assetId = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        WorkOrderSort sort = WorkOrderSort.CreatedAt,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject, the same as every other list in this API.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.WorkOrders.AsNoTracking();

        // THE VISIBILITY SCOPE GOES ON FIRST, before the filters and before the count, so
        // no ordering of the clauses below can let a row out. A Technician asking for
        // another technician's id gets an empty page, not that technician's queue.
        if (!SeesEveryWorkOrder(callerRole))
        {
            query = query.Where(w => w.AssignedTechnicianId == callerId);
        }

        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }

        if (technicianId is not null)
        {
            query = query.Where(w => w.AssignedTechnicianId == technicianId);
        }

        if (assetId is not null)
        {
            query = query.Where(w => w.AssetId == assetId);
        }

        // Calendar dates turned into UTC bounds here, in C#, so only DateTime reaches the
        // query — identical to the report list, including the exclusive day-after that
        // makes dateTo inclusive.
        if (dateFrom is not null)
        {
            var from = dateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(w => w.CreatedAt >= from);
        }

        if (dateTo is not null)
        {
            var toExclusive = dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(w => w.CreatedAt < toExclusive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        List<WorkOrderDto> items;

        if (sort == WorkOrderSort.Cost)
        {
            // SORTED IN MEMORY, AFTER THE FILTERS. SQLite has no decimal type and will not
            // ORDER BY one, and a cast to double in the query would put money through a
            // binary float to rank it. The filtered set is materialised, ordered here in
            // C# as decimal, and paged. A campus has hundreds of live orders, not millions,
            // so this costs nothing that matters — and it behaves identically on both test
            // databases, which a provider-specific ORDER BY would not.
            var rows = await query.Select(ToDtoExpression).ToListAsync(cancellationToken);

            items = rows
                .OrderByDescending(w => w.EstimatedCost)
                .ThenByDescending(w => w.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            // Id breaks every tie: orders raised in one SaveChanges share a CreatedAt, and
            // without a total order a row could land on two pages or on none.
            items = await query
                .OrderByDescending(w => w.CreatedAt)
                .ThenByDescending(w => w.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToDtoExpression)
                .ToListAsync(cancellationToken);
        }

        return new PagedResult<WorkOrderDto>(items, page, pageSize, totalCount);
    }

    public async Task<WorkOrderDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders
            .AsNoTracking()
            .Include(w => w.Report)
            .Include(w => w.Asset)
            .Include(w => w.AssignedTechnician)
            .Include(w => w.ApprovedBy)
            .Include(w => w.ScheduledSlots)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        // The same rule as the list, applied here rather than assumed.
        if (!SeesEveryWorkOrder(callerRole) && order.AssignedTechnicianId != callerId)
        {
            return null;
        }

        return new WorkOrderDetailDto(
            order.Id,
            order.ReportId,
            order.Report!.Description,
            ToAssetDto(order.Asset!),
            order.AssignedTechnician is null ? null : ToUserDto(order.AssignedTechnician),
            order.Status,
            order.Strategy,
            order.EstimatedCost,
            order.ActualCost,
            order.PartsRequired,
            order.ResolutionNote,
            order.CompletionPhotoUrl,
            order.ApprovedBy is null ? null : ToUserDto(order.ApprovedBy),
            order.ApprovedAt,
            order.RejectionReason,
            order.RevisionNote,
            order.CompletedAt,
            order.CreatedAt,
            order.UpdatedAt,
            // Oldest booking first; Id is monotonic per insert, so it orders the way the
            // bookings were made without ties.
            order.ScheduledSlots
                .OrderBy(s => s.Id)
                .Select(s => new ScheduledSlotDto(
                    s.Id, s.WorkOrderId, s.StartsAt, s.EndsAt, s.CreatedAt, s.UpdatedAt))
                .ToList());
    }

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) =>
        _db.WorkOrders.AnyAsync(w => w.Id == id, cancellationToken);

    public async Task<CreateWorkOrderResult> CreateAsync(
        CreateWorkOrderDto dto,
        CancellationToken cancellationToken = default)
    {
        // Both checked here rather than left to the foreign keys, so a bad id is a 400
        // naming its field instead of a 500 out of the database driver.
        var report = await _db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == dto.ReportId, cancellationToken);

        if (report is null)
        {
            return new CreateWorkOrderResult(CreateWorkOrderOutcome.ReportNotFound);
        }

        var asset = await _db.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == dto.AssetId, cancellationToken);

        if (asset is null)
        {
            return new CreateWorkOrderResult(CreateWorkOrderOutcome.AssetNotFound);
        }

        if (report.Status == ReportStatus.Closed)
        {
            return new CreateWorkOrderResult(CreateWorkOrderOutcome.ReportClosed);
        }

        // [Required] on the DTO has already refused a missing value with a 400, so these
        // are never null by the time they get here.
        var estimatedCost = dto.EstimatedCost!.Value;
        var strategy = dto.Strategy!.Value;

        var needsApproval = RequiresApproval(estimatedCost, strategy);

        // Raised as Draft in principle (see CreateWorkOrderDto), and routed out of it in
        // the same breath: the gate is decided before the row is ever written, so no
        // reader can catch an order sitting in Draft waiting to be routed.
        //
        // An order that did not need a decision has no ApprovedBy and no ApprovedAt. Null
        // there means "nobody had to decide", and Status is what says it is approved.
        var order = new WorkOrder
        {
            ReportId = dto.ReportId,
            AssetId = dto.AssetId,
            Strategy = strategy,
            EstimatedCost = estimatedCost,
            PartsRequired = dto.PartsRequired,
            Status = needsApproval ? WorkOrderStatus.AwaitingApproval : WorkOrderStatus.Approved
        };

        _db.WorkOrders.Add(order);

        // The workflow moves in the SAME SaveChanges as the order, so the two cannot
        // disagree — a workflow never says AwaitingManagerApproval with no order waiting,
        // nor WorkOrderRaised with none raised. Same rule as ClarificationService moving a
        // report alongside its questions.
        var workflow = await LatestWorkflowForReportAsync(dto.ReportId, cancellationToken);

        if (workflow is not null)
        {
            workflow.CurrentState = needsApproval
                ? WorkflowState.AwaitingManagerApproval
                : WorkflowState.WorkOrderRaised;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CreateWorkOrderResult(
            CreateWorkOrderOutcome.Success,
            new WorkOrderDto(
                order.Id, order.ReportId, order.AssetId, asset.AssetTag,
                order.AssignedTechnicianId, null, order.Status, order.Strategy,
                order.EstimatedCost, order.ActualCost, order.CompletedAt,
                order.CreatedAt, order.UpdatedAt));
    }

    public async Task<WorkOrderActionOutcome> AssignAsync(
        int id,
        int technicianId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return WorkOrderActionOutcome.NotFound;
        }

        // State before content, the same order as ClarificationService: an order waiting
        // on a manager is not assignable to anybody, so there is no point checking who.
        if (!ActiveStatuses.Contains(order.Status))
        {
            return WorkOrderActionOutcome.InvalidState;
        }

        // A Technician, not merely a user. Handing a repair to a Reporter would put it in
        // a queue nobody reads, since only a Technician can complete one.
        if (!await IsTechnicianAsync(technicianId, cancellationToken))
        {
            return WorkOrderActionOutcome.NotATechnician;
        }

        order.AssignedTechnicianId = technicianId;

        await _db.SaveChangesAsync(cancellationToken);
        return WorkOrderActionOutcome.Success;
    }

    public async Task<WorkOrderActionOutcome> CompleteAsync(
        int id,
        int callerId,
        CompleteWorkOrderDto dto,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders
            .Include(w => w.AssignedTechnician)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return WorkOrderActionOutcome.NotFound;
        }

        // Identity before state: another technician learns nothing about where this order
        // has got to. An unassigned order is nobody's to complete, so this covers it too.
        if (order.AssignedTechnicianId != callerId)
        {
            return WorkOrderActionOutcome.NotAssignedToCaller;
        }

        if (!ActiveStatuses.Contains(order.Status))
        {
            return WorkOrderActionOutcome.InvalidState;
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // ONE REAL TRANSACTION, because this is more than one SaveChanges: the verification
        // check below is raised by VerificationService, which reads the order back as
        // Completed and saves on its own. Everything written between here and Commit lands
        // together or not at all. A completed order with no service record is a hole in
        // the history the diagnostic agent reads; a service record with no completed order
        // is an orphan claiming work nobody finished.
        //
        // `await using` rolls back on dispose if Commit is never reached — an exception
        // anywhere below leaves the database exactly as it was.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        order.Status = WorkOrderStatus.Completed;
        order.ActualCost = dto.ActualCost!.Value;
        order.ResolutionNote = dto.ResolutionNote;
        order.CompletionPhotoUrl = dto.CompletionPhotoUrl;
        order.CompletedAt = now;

        // APPENDED, never updated: this is the immutable history. The note goes in
        // verbatim — the wording is the evidence, so it is not trimmed or tidied.
        //
        // ServicedOn is the UTC calendar date of the same instant as CompletedAt, read from
        // the same TimeProvider as the failure summary, so the two can be pinned together
        // in a test.
        _db.ServiceRecords.Add(new ServiceRecord
        {
            AssetId = order.AssetId,
            ServicedOn = DateOnly.FromDateTime(now),
            TechnicianName = order.AssignedTechnician!.FullName,
            TechnicianNote = dto.ResolutionNote,
            Outcome = dto.Outcome!.Value,
            WorkOrderId = order.Id
        });

        var workflow = await LatestWorkflowForReportAsync(order.ReportId, cancellationToken);

        if (workflow is not null)
        {
            workflow.CurrentState = WorkflowState.AwaitingVerification;
        }

        await _db.SaveChangesAsync(cancellationToken);

        // AwaitingVerification has to be waiting ON something. The check falls due
        // VerificationSettings.DelayDays after CompletedAt — see VerificationService.
        var check = await _verificationService.CreateForCompletedWorkOrderAsync(order.Id, cancellationToken);

        if (check is null)
        {
            // Unreachable: the order was completed a moment ago in this transaction, and an
            // order is only ever completed once, so it cannot already have an open check.
            // Thrown rather than returned, so the transaction rolls back and nothing above
            // survives on its own.
            throw new InvalidOperationException(
                $"Could not raise a verification check for work order {order.Id} after completing it.");
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkOrderActionOutcome.Success;
    }

    public async Task<WorkOrderActionOutcome> ApproveAsync(
        int id,
        int managerId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return WorkOrderActionOutcome.NotFound;
        }

        // Only an order that is actually waiting on a manager. Approving one that was
        // auto-approved, or one already rejected, would rewrite a decision that was made.
        if (order.Status != WorkOrderStatus.AwaitingApproval)
        {
            return WorkOrderActionOutcome.InvalidState;
        }

        order.Status = WorkOrderStatus.Approved;
        order.ApprovedByUserId = managerId;
        order.ApprovedAt = _time.GetUtcNow().UtcDateTime;

        var workflow = await LatestWorkflowForReportAsync(order.ReportId, cancellationToken);

        if (workflow is not null)
        {
            workflow.CurrentState = WorkflowState.WorkOrderRaised;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WorkOrderActionOutcome.Success;
    }

    public async Task<WorkOrderActionOutcome> RejectAsync(
        int id,
        int managerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return WorkOrderActionOutcome.NotFound;
        }

        if (order.Status != WorkOrderStatus.AwaitingApproval)
        {
            return WorkOrderActionOutcome.InvalidState;
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // ApprovedBy/ApprovedAt record who DECIDED and when, whichever way it went — see
        // WorkOrder.ApprovedByUserId. Status is what says which way.
        order.Status = WorkOrderStatus.Rejected;
        order.RejectionReason = reason;
        order.ApprovedByUserId = managerId;
        order.ApprovedAt = now;

        var workflow = await LatestWorkflowForReportAsync(order.ReportId, cancellationToken);

        if (workflow is not null)
        {
            workflow.CurrentState = WorkflowState.Closed;
            workflow.CompletedAt = now;
            workflow.Outcome = $"Work order {order.Id} was rejected by a facilities manager: {reason}";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WorkOrderActionOutcome.Success;
    }

    public async Task<WorkOrderActionOutcome> RequestRevisionAsync(
        int id,
        string note,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return WorkOrderActionOutcome.NotFound;
        }

        if (order.Status != WorkOrderStatus.AwaitingApproval)
        {
            return WorkOrderActionOutcome.InvalidState;
        }

        var workflow = await LatestWorkflowForReportAsync(order.ReportId, cancellationToken);

        // Without a workflow there is no Strategist run to re-queue, and the order would sit
        // in Draft with a note nothing ever reads. Refused rather than accepted into a dead
        // end; the manager can still reject it.
        if (workflow is null)
        {
            return WorkOrderActionOutcome.NoWorkflow;
        }

        // Back to Draft rather than Cancelled or Rejected: the work is still wanted, just
        // not planned like this. The note is where the Strategist will read what to change.
        order.Status = WorkOrderStatus.Draft;
        order.RevisionNote = note;

        workflow.CurrentState = WorkflowState.Strategizing;

        await _db.SaveChangesAsync(cancellationToken);

        // Re-queued AFTER the save, so the runner can never dequeue an id whose state has
        // not been written yet. CancellationToken.None, not the request's: that token is
        // cancelled as soon as the response is written, which would abort the hand-off.
        //
        // NOTE: WorkflowRunner.BeginProcessingAsync only starts a workflow in Submitted, so
        // today the runner logs a warning and skips this item — there is no Strategist
        // agent yet. The hand-off is made anyway, so the resume point already sits where
        // it belongs, the same as the clarification resume.
        await _workflowQueue.EnqueueAsync(workflow.Id, CancellationToken.None);

        return WorkOrderActionOutcome.Success;
    }

    public async Task<AvailableSlotsResult> GetAvailableSlotsAsync(
        int assetId,
        int? technicianId,
        int durationMinutes,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate || toDate.DayNumber - fromDate.DayNumber >= MaxSlotSearchDays)
        {
            return new AvailableSlotsResult(AvailableSlotsOutcome.InvalidDateRange);
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);

        if (duration > _scheduling.WorkdayEnd - _scheduling.WorkdayStart)
        {
            return new AvailableSlotsResult(AvailableSlotsOutcome.DurationTooLong);
        }

        // The room is what the timetable is kept against, and the asset is how the caller
        // names it. A projector is worked on where it hangs.
        var roomId = await _db.Assets
            .Where(a => a.Id == assetId)
            .Select(a => (int?)a.RoomId)
            .FirstOrDefaultAsync(cancellationToken);

        if (roomId is null)
        {
            return new AvailableSlotsResult(AvailableSlotsOutcome.AssetNotFound);
        }

        if (technicianId is not null && !await IsTechnicianAsync(technicianId.Value, cancellationToken))
        {
            return new AvailableSlotsResult(AvailableSlotsOutcome.NotATechnician);
        }

        // The search window in UTC: local midnight at the start of fromDate to local midnight
        // after toDate. Only used to decide what to LOAD — which candidates exist is
        // SlotRules' business, from the same two dates.
        var zone = _scheduling.TimeZone;
        var windowStart = TimeZoneInfo.ConvertTimeToUtc(fromDate.ToDateTime(TimeOnly.MinValue), zone);
        var windowEnd = TimeZoneInfo.ConvertTimeToUtc(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);

        var busy = await LoadBusyIntervalsAsync(roomId.Value, technicianId, windowStart, windowEnd, cancellationToken);

        var slots = SlotRules.FindFreeSlots(
            fromDate, toDate, duration, busy, _time.GetUtcNow().UtcDateTime, _scheduling);

        return new AvailableSlotsResult(AvailableSlotsOutcome.Success, slots);
    }

    public async Task<ScheduleWorkOrderResult> ScheduleAsync(
        int id,
        ScheduleWorkOrderDto dto,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders
            .Include(w => w.Asset)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (order is null)
        {
            return new ScheduleWorkOrderResult(ScheduleOutcome.NotFound);
        }

        // State before content, the same order as assign and complete.
        if (!ActiveStatuses.Contains(order.Status))
        {
            return new ScheduleWorkOrderResult(ScheduleOutcome.InvalidState);
        }

        if (order.AssignedTechnicianId is null)
        {
            return new ScheduleWorkOrderResult(ScheduleOutcome.NoTechnicianAssigned);
        }

        // [Required] has refused a missing value already. A time with no offset is refused
        // here: it could have been read off any clock, and guessing is how a visit lands
        // five and a half hours from where the manager put it. One with an offset is
        // converted, so "Z" and "+05:30" both work.
        if (dto.StartsAt!.Value.Kind == DateTimeKind.Unspecified
            || dto.EndsAt!.Value.Kind == DateTimeKind.Unspecified)
        {
            return new ScheduleWorkOrderResult(ScheduleOutcome.NotBookable);
        }

        var startsAt = dto.StartsAt.Value.ToUniversalTime();
        var endsAt = dto.EndsAt.Value.ToUniversalTime();

        // THE RE-CHECK AND THE INSERT ARE ONE SERIALIZABLE TRANSACTION. Re-running the check
        // is what catches a slot taken in the thirty seconds since it was offered. Doing it
        // serializably is what catches two managers booking the same technician in the same
        // instant: each would read "free" and insert, and neither insert conflicts with a
        // row the other can see. Under SERIALIZABLE, PostgreSQL notices that the two reads
        // and writes cannot both have happened in some order and aborts one of them —
        // which becomes the same 409 as any other taken slot. SQLite serializes every write
        // transaction anyway.
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var busy = await LoadBusyIntervalsAsync(
                order.Asset!.RoomId, order.AssignedTechnicianId, startsAt, endsAt, cancellationToken);

            // The very function the offer list was built with — not a booking-side copy of it.
            var check = SlotRules.Check(startsAt, endsAt, busy, _time.GetUtcNow().UtcDateTime, _scheduling);

            if (check == SlotCheck.Conflict)
            {
                return new ScheduleWorkOrderResult(ScheduleOutcome.SlotTaken);
            }

            if (check != SlotCheck.Free)
            {
                return new ScheduleWorkOrderResult(ScheduleOutcome.NotBookable);
            }

            var slot = new ScheduledSlot { WorkOrderId = order.Id, StartsAt = startsAt, EndsAt = endsAt };
            _db.ScheduledSlots.Add(slot);

            // Approved -> Scheduled: assigned and now booked. An order already Scheduled or
            // InProgress is booking another visit, and stays where it is.
            if (order.Status == WorkOrderStatus.Approved)
            {
                order.Status = WorkOrderStatus.Scheduled;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ScheduleWorkOrderResult(
                ScheduleOutcome.Success,
                new ScheduledSlotDto(slot.Id, slot.WorkOrderId, slot.StartsAt, slot.EndsAt, slot.CreatedAt, slot.UpdatedAt));
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            // The concurrent booking won. Nothing of ours was committed.
            return new ScheduleWorkOrderResult(ScheduleOutcome.SlotTaken);
        }
    }

    /// <summary>
    /// Everything that makes time unavailable in a room — its classes, widened by the
    /// buffer — and, when a technician is named, for that technician: their visits on
    /// orders still live. A cancelled or rejected order's bookings free the diary again.
    ///
    /// THE QUERY ONLY DECIDES WHAT TO LOAD, NEVER WHAT IS FREE. Its bounds are widened by a
    /// whole day either side, so an off-by-one here can only fetch a row too many and never
    /// drop one that matters; the boundary decision is SlotRules.Overlaps, in C#, and
    /// nowhere else.
    /// </summary>
    private async Task<IReadOnlyList<BusyInterval>> LoadBusyIntervalsAsync(
        int roomId,
        int? technicianId,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken)
    {
        var loadFrom = windowStart.AddDays(-1);
        var loadTo = windowEnd.AddDays(1);

        var classes = await _db.ClassScheduleSlots
            .AsNoTracking()
            .Where(c => c.RoomId == roomId && c.StartsAt < loadTo && c.EndsAt > loadFrom)
            .Select(c => new { c.StartsAt, c.EndsAt })
            .ToListAsync(cancellationToken);

        var busy = classes
            .Select(c => SlotRules.ClassWithBuffer(c.StartsAt, c.EndsAt, _scheduling))
            .ToList();

        if (technicianId is not null)
        {
            var visits = await _db.ScheduledSlots
                .AsNoTracking()
                .Where(s => s.WorkOrder!.AssignedTechnicianId == technicianId
                         && ActiveStatuses.Contains(s.WorkOrder.Status)
                         && s.StartsAt < loadTo && s.EndsAt > loadFrom)
                .Select(s => new { s.StartsAt, s.EndsAt })
                .ToListAsync(cancellationToken);

            busy.AddRange(visits.Select(v => new BusyInterval(v.StartsAt, v.EndsAt)));
        }

        return busy;
    }

    private Task<bool> IsTechnicianAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(u => u.Id == userId && u.Role == Role.Technician, cancellationToken);

    /// <summary>
    /// PostgreSQL's "could not serialize access" (SQLSTATE 40001), which it raises on the
    /// losing side of a concurrent booking — from SaveChanges wrapped in a
    /// DbUpdateException, or from Commit directly. Read off DbException.SqlState so the
    /// check names no provider type.
    /// </summary>
    private static bool IsSerializationFailure(Exception ex) =>
        ex is DbException { SqlState: "40001" }
        || ex.InnerException is DbException { SqlState: "40001" };

    /// <summary>
    /// The workflow a work order's state changes are applied to: the most recent one raised
    /// for its report. A report can be clarified by more than one run over its life, and
    /// the latest is the one still carrying the fault forward.
    ///
    /// Null for a report with no workflow at all — the seeded history, or anything created
    /// before the report intake raised one. The order's own transition still happens; there
    /// is simply no run to move alongside it.
    /// </summary>
    private Task<AgentWorkflow?> LatestWorkflowForReportAsync(
        int reportId,
        CancellationToken cancellationToken) =>
        _db.AgentWorkflows
            .Where(w => w.ReportId == reportId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The list row, as an expression so EF translates it into the SELECT — the asset tag
    /// and technician name come back as columns of the same query, not a lookup per row.
    /// </summary>
    private static readonly Expression<Func<WorkOrder, WorkOrderDto>> ToDtoExpression =
        w => new WorkOrderDto(
            w.Id,
            w.ReportId,
            w.AssetId,
            w.Asset!.AssetTag,
            w.AssignedTechnicianId,
            w.AssignedTechnician == null ? null : w.AssignedTechnician.FullName,
            w.Status,
            w.Strategy,
            w.EstimatedCost,
            w.ActualCost,
            w.CompletedAt,
            w.CreatedAt,
            w.UpdatedAt);

    /// <summary>
    /// Written here rather than reached for across services, for the same reason as
    /// ReportService.ToAssetDto: AssetService's mapper is private to it.
    /// </summary>
    private static AssetDto ToAssetDto(Asset a) =>
        new(a.Id, a.AssetTag, a.Name, a.AssetCategoryId, a.RoomId, a.Manufacturer, a.Model,
            a.InstalledOn, a.WarrantyExpiresOn, a.Status, a.CreatedAt, a.UpdatedAt);

    private static UserDto ToUserDto(User u) => new(u.Id, u.Email, u.FullName, u.Role);
}
