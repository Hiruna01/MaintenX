using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IWorkOrderService
{
    /// <summary>
    /// The most work orders `get_open_work_orders` will return.
    ///
    /// A constant, not a field on <see cref="ToolCallRequest"/>, for the same reason as
    /// IReportService.MaxToolRelatedReports: the agent names a tool and an id, and how much
    /// one call can pull is not the caller's to widen.
    /// </summary>
    const int MaxToolOpenWorkOrders = 10;

    /// <summary>
    /// One page of work orders, filtered and sorted, through the existing PagedResult&lt;T&gt;.
    ///
    /// WHO MAY SEE WHAT IS DECIDED HERE, NOT BY THE CALLER. A FacilitiesManager and an Admin
    /// see every order; everybody else sees only the orders assigned to them — which for a
    /// Technician is their queue, and for a Reporter is nothing. Applied as a Where before
    /// the count and before paging, the same shape as IReportService.GetAllAsync.
    ///
    /// <paramref name="dateFrom"/> and <paramref name="dateTo"/> are calendar dates against
    /// CreatedAt and BOTH ENDS ARE INCLUSIVE, exactly as on the report list.
    /// </summary>
    Task<PagedResult<WorkOrderDto>> GetAllAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One work order in full. The same visibility rule as <see cref="GetAllAsync"/>, and
    /// it has to be — a list that hides other technicians' orders while this hands one over
    /// by id would be a rule that only looks enforced. Null both when the order does not
    /// exist and when the caller may not see it; <see cref="ExistsAsync"/> tells those apart.
    /// </summary>
    Task<WorkOrderDetailDto?> GetDetailAsync(
        int id,
        int callerId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a work order exists AT ALL, ignoring who is asking.</summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Work orders still open — not Completed, Rejected or Cancelled — on ANY asset in the
    /// same room as this one, this asset's own included. Newest first, capped at
    /// <see cref="MaxToolOpenWorkOrders"/>. What `get_open_work_orders` returns.
    ///
    /// One id and two questions: "is work already open on this machine" and "is anyone
    /// already going to this room". The second is the one consolidation needs — a
    /// technician in the room for the projector can look at the air conditioner in the same
    /// visit — and a room is reached through an asset because every strategist tool takes
    /// the asset's id.
    ///
    /// No visibility scope: the caller is the agent service, behind the shared secret, not
    /// a user with a role. Facts, never a judgement — nothing here says which orders SHOULD
    /// be combined; that is the strategist's proposal, and C# decides what is raised.
    ///
    /// NULL means no asset has that id; an EMPTY LIST means nothing is open in its room —
    /// the same null-versus-empty rule as every other tool.
    /// </summary>
    Task<IReadOnlyList<WorkOrderDto>?> GetOpenWorkOrdersInAssetRoomAsync(
        int assetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises a work order from a report and routes it through THE APPROVAL GATE:
    /// an estimate above ApprovalSettings.CostThreshold, or a strategy of
    /// EscalateReplacement, goes to AwaitingApproval and moves the report's workflow to
    /// AwaitingManagerApproval; anything else is Approved on the spot and the workflow moves
    /// to WorkOrderRaised. Both in the same SaveChanges as the order.
    ///
    /// The comparison is decimal against decimal, in C#, before anything is written — never
    /// in SQL (SQLite compares these as text) and never in a prompt.
    /// </summary>
    Task<CreateWorkOrderResult> CreateAsync(
        CreateWorkOrderDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands the order to a technician. Only an order that has cleared approval can be
    /// assigned — Approved, Scheduled or InProgress, the last two being a reassignment —
    /// and only to a user whose role is Technician. Does not book a time; see
    /// AssignTechnicianDto.
    /// </summary>
    Task<WorkOrderActionOutcome> AssignAsync(
        int id,
        int technicianId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finishes the work, IN ONE DATABASE TRANSACTION: the order goes to Completed, a
    /// ServiceRecord is appended to the asset's history, a VerificationCheck is raised,
    /// and the workflow moves to AwaitingVerification. All of it or none of it.
    ///
    /// Only the technician the order is assigned to may complete it; the check is on the
    /// caller's id from the token, so a Technician policy alone is not enough.
    /// </summary>
    Task<WorkOrderActionOutcome> CompleteAsync(
        int id,
        int callerId,
        CompleteWorkOrderDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A manager's yes: Approved, with who and when recorded, and the workflow to
    /// WorkOrderRaised. Only from AwaitingApproval.
    /// </summary>
    Task<WorkOrderActionOutcome> ApproveAsync(
        int id,
        int managerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A manager's no: Rejected, with who, when and why recorded, and the workflow Closed.
    /// Only from AwaitingApproval. That the reason is present is enforced by
    /// RejectWorkOrderDto before this is called.
    /// </summary>
    Task<WorkOrderActionOutcome> RejectAsync(
        int id,
        int managerId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// "Not like this": the order goes back to Draft with the manager's note on it, the
    /// workflow goes back to Strategizing, and the workflow id is re-queued so the
    /// Strategist runs again. Only from AwaitingApproval, and only when the report has a
    /// workflow to re-run — without one nothing would ever pick the note up.
    /// </summary>
    Task<WorkOrderActionOutcome> RequestRevisionAsync(
        int id,
        string note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Free blocks of <paramref name="durationMinutes"/> for work on an asset, between two
    /// campus-local calendar dates (both inclusive), earliest first, at most twenty.
    ///
    /// Free means: inside the working day (SchedulingSettings), not already started, no
    /// class in the asset's room within the buffer either side, and — when
    /// <paramref name="technicianId"/> is given — no other visit booked for that technician.
    /// All of it is decided in C# by SlotRules; an agent may propose a time, never decide
    /// that one is free.
    ///
    /// An OFFER, not a reservation: two managers can be shown the same block, which is why
    /// <see cref="ScheduleAsync"/> runs the same check again.
    /// </summary>
    Task<AvailableSlotsResult> GetAvailableSlotsAsync(
        int assetId,
        int? technicianId,
        int durationMinutes,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Books one visit for the order: a new ScheduledSlot row, and the order to Scheduled if
    /// it was Approved. The availability check is RE-RUN here against the order's own asset
    /// and assigned technician, inside a serializable transaction together with the insert,
    /// so neither a slot taken since it was offered nor two managers booking the same
    /// technician at the same moment can produce a double booking.
    /// </summary>
    Task<ScheduleWorkOrderResult> ScheduleAsync(
        int id,
        ScheduleWorkOrderDto dto,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Why a <see cref="IWorkOrderService.CreateAsync"/> call ended as it did. A plain enum, not
/// a Result&lt;T&gt; — same shape as AttachPhotoOutcome.
/// </summary>
public enum CreateWorkOrderOutcome
{
    /// <summary>Raised and routed. A 201.</summary>
    Success,

    /// <summary>No report has that id. A 400 against ReportId.</summary>
    ReportNotFound,

    /// <summary>No asset has that id. A 400 against AssetId.</summary>
    AssetNotFound,

    /// <summary>
    /// The report is Closed. A 409: Closed is terminal, and raising work against it would
    /// also drag its finished workflow back into the lifecycle. A fault that has come back
    /// is a new report.
    /// </summary>
    ReportClosed
}

/// <summary><see cref="WorkOrder"/> is set only on <see cref="CreateWorkOrderOutcome.Success"/>.</summary>
public record CreateWorkOrderResult(CreateWorkOrderOutcome Outcome, WorkOrderDto? WorkOrder = null);

/// <summary>
/// Why one of the work order ACTIONS (assign, complete, approve, reject, request-revision)
/// ended as it did. One enum for all five rather than one each, because they fail in the
/// same few ways and the controller maps each to the same status code every time.
/// </summary>
public enum WorkOrderActionOutcome
{
    /// <summary>Done. A 204.</summary>
    Success,

    /// <summary>No work order has that id. A 404.</summary>
    NotFound,

    /// <summary>
    /// Complete only: the caller is a Technician, but not the one this order is assigned
    /// to. A 403 — we know who they are, and it is not their job.
    /// </summary>
    NotAssignedToCaller,

    /// <summary>
    /// The order is not in a status this action can move it out of — approving something
    /// not AwaitingApproval, completing something already Completed. A 409: the request is
    /// well formed, and it is the order that is not where the caller thinks it is.
    /// </summary>
    InvalidState,

    /// <summary>Assign only: the named user does not exist or is not a Technician. A 400.</summary>
    NotATechnician,

    /// <summary>
    /// Request-revision only: the report has no agent workflow to send back, so there is no
    /// Strategist run that could ever read the note. A 409.
    /// </summary>
    NoWorkflow
}

/// <summary>
/// Why a <see cref="IWorkOrderService.GetAvailableSlotsAsync"/> call ended as it did. Every
/// failure is a 400: they are all about the query the caller sent.
/// </summary>
public enum AvailableSlotsOutcome
{
    Success,

    /// <summary>No asset has that id — there is no room to check.</summary>
    AssetNotFound,

    /// <summary>The technician named does not exist or is not a Technician.</summary>
    NotATechnician,

    /// <summary>toDate is before fromDate, or the range is longer than the search allows.</summary>
    InvalidDateRange,

    /// <summary>The job is longer than the working day, so no slot could ever hold it.</summary>
    DurationTooLong
}

/// <summary><see cref="Slots"/> is set only on <see cref="AvailableSlotsOutcome.Success"/>.</summary>
public record AvailableSlotsResult(
    AvailableSlotsOutcome Outcome,
    IReadOnlyList<AvailableSlotDto>? Slots = null);

/// <summary>Why a <see cref="IWorkOrderService.ScheduleAsync"/> call ended as it did.</summary>
public enum ScheduleOutcome
{
    /// <summary>Booked. A 201 carrying the new slot.</summary>
    Success,

    /// <summary>No work order has that id. A 404.</summary>
    NotFound,

    /// <summary>
    /// The order has not cleared approval, or is already finished. A 409 — the same rule as
    /// assign and complete.
    /// </summary>
    InvalidState,

    /// <summary>
    /// Nobody is assigned yet. A 409: a booking is time in somebody's diary, and Scheduled
    /// means assigned AND booked, so assign first.
    /// </summary>
    NoTechnicianAssigned,

    /// <summary>
    /// The slot could never have been offered: outside the working day, already started, an
    /// end not after its start, or a time with no UTC offset. A 400.
    /// </summary>
    NotBookable,

    /// <summary>
    /// The slot overlaps a buffered class or another of the technician's visits — taken
    /// since it was offered, or lost to a concurrent booking. A 409.
    /// </summary>
    SlotTaken
}

/// <summary><see cref="Slot"/> is set only on <see cref="ScheduleOutcome.Success"/>.</summary>
public record ScheduleWorkOrderResult(ScheduleOutcome Outcome, ScheduledSlotDto? Slot = null);
