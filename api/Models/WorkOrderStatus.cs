namespace CampusFacilities.Api.Models;

/// <summary>
/// Where a <see cref="WorkOrder"/> has got to. Persisted as a string in PostgreSQL (see
/// AppDbContext), same as <see cref="Role"/> and <see cref="ReportStatus"/>, so a row
/// reads "AwaitingApproval" rather than "1".
///
/// This is the SCHEDULING LIFECYCLE, and it is the reason a work order is not a
/// <see cref="ServiceRecord"/>: these states exist precisely because the work is still
/// changing. Once it stops changing, Completed is the end of this table's interest and a
/// ServiceRecord is appended to carry the history forward.
///
/// Not a copy of <see cref="ReportStatus"/> either. One report can raise more than one
/// work order over its life (an inspection first, then the repair it recommends), so the
/// fault's status and any one order's status answer different questions.
///
/// Every transition between these is a deterministic business rule — which ones are legal,
/// and which one a cost crossing the approval threshold forces — and belongs in C#, never
/// in a model prompt. See ApprovalSettings for the threshold itself.
/// </summary>
public enum WorkOrderStatus
{
    /// <summary>Raised but not yet submitted for approval or assignment.</summary>
    Draft,

    /// <summary>
    /// Estimated cost crossed the approval threshold, so a manager has to decide before
    /// any work is scheduled. An order below the threshold never passes through here.
    /// </summary>
    AwaitingApproval,

    Approved,

    /// <summary>
    /// A manager said no. Terminal, and distinct from Cancelled: this records that the
    /// work was judged and refused, with the reason on WorkOrder.RejectionReason.
    /// </summary>
    Rejected,

    /// <summary>Assigned to a technician and booked into a ScheduledSlot.</summary>
    Scheduled,

    InProgress,

    /// <summary>
    /// The work is done and will not change again. This is the state that appends a
    /// ServiceRecord against the asset.
    /// </summary>
    Completed,

    /// <summary>
    /// Called off before it was carried out — the fault cleared itself, the equipment was
    /// retired, the order was raised twice. Not the same as Rejected, which is a decision
    /// about the work rather than the disappearance of the need for it.
    /// </summary>
    Cancelled
}
