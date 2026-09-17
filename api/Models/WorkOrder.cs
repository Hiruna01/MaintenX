using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// Live maintenance work: what is to be done, to which machine, by whom, for how much,
/// and how far through it is.
///
/// THIS IS DELIBERATELY NOT THE SAME TABLE AS <see cref="ServiceRecord"/>. A work order
/// is in progress — assigned, re-assigned, approved, rescheduled, still changing. A
/// ServiceRecord is what is left once the work is finished and will not change again, and
/// this table APPENDS one when an order reaches <see cref="WorkOrderStatus.Completed"/>.
///
/// They stay separate because they are read for different reasons. The diagnostic agent
/// reads service history to find repeat failures across months; it has no business seeing
/// half-finished work, and that history must not shift under it every time a technician
/// touches a live order. Merging them would also put a scheduling lifecycle and an
/// immutable record in one table, which is two jobs.
/// </summary>
public class WorkOrder
{
    public int Id { get; set; }

    /// <summary>
    /// The fault this work answers. Not nullable: work is done because something was
    /// reported, and an order with no report behind it has no fault to close.
    ///
    /// One report can raise several orders over its life — an inspection, then the repair
    /// that inspection recommends — which is why the report's own status is not this
    /// status. See <see cref="ReportStatus"/>.
    /// </summary>
    public int ReportId { get; set; }

    public Report? Report { get; set; }

    /// <summary>
    /// The equipment being worked on. NOT NULLABLE, unlike <see cref="Report.AssetId"/>,
    /// and the difference is the point: a reporter is not expected to know which asset tag
    /// the misbehaving projector carries, but nobody can be sent to repair a machine
    /// nobody has identified. Whatever triage fills in on the report is settled by the
    /// time an order is raised from it.
    ///
    /// It is also what makes the completed <see cref="ServiceRecord"/> land against the
    /// right service history — the history the diagnostic agent reads.
    /// </summary>
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>
    /// The technician who will do the work, or null while it is unassigned — which every
    /// order is when it is raised, and which an order stays while it waits for approval.
    ///
    /// Whether a given technician is actually available at a given time is a timetable
    /// question answered in C# against ScheduledSlot and ClassScheduleSlot, never by an
    /// agent.
    /// </summary>
    public int? AssignedTechnicianId { get; set; }

    public User? AssignedTechnician { get; set; }

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;

    public WorkOrderStrategy Strategy { get; set; }

    /// <summary>
    /// What the work is expected to cost, in LKR.
    ///
    /// DECIMAL, NEVER double OR float. This column is compared against the approval
    /// threshold (see ApprovalSettings), and binary floating point cannot hold ordinary
    /// decimal amounts exactly — a value that should sit exactly on the threshold can
    /// land a hair below it and auto-approve work that was supposed to reach a manager.
    /// A rounding error here is not a display problem, it is money spent without
    /// authorisation. decimal is base-10 and stores these amounts exactly.
    /// </summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>
    /// What it actually cost, in LKR. Null until the work completes, and decimal for the
    /// same reason as <see cref="EstimatedCost"/> — it is the figure any overspend rule
    /// compares against the estimate.
    /// </summary>
    public decimal? ActualCost { get; set; }

    /// <summary>
    /// Parts the job needs, as the technician or planner wrote them. Free text because a
    /// parts catalogue is a component this project does not have; null when the job needs
    /// none.
    /// </summary>
    [MaxLength(1000)]
    public string? PartsRequired { get; set; }

    /// <summary>
    /// What was actually done, written at completion. This is the text copied into the
    /// <see cref="ServiceRecord.TechnicianNote"/> the completed order appends, which is
    /// what the diagnostic agent later reads — so it is stored verbatim and never tidied.
    /// </summary>
    [MaxLength(2000)]
    public string? ResolutionNote { get; set; }

    /// <summary>
    /// Evidence the work was done, as a URL to wherever the image is stored — never the
    /// image bytes, same rule as <see cref="Report.PhotoUrl"/>.
    /// </summary>
    [MaxLength(500)]
    public string? CompletionPhotoUrl { get; set; }

    /// <summary>
    /// The manager who approved or rejected it, and when. Null on an order that never
    /// needed a decision, which is every order whose estimate sat under the threshold —
    /// so null here does NOT mean "waiting", and the status is what says that.
    /// </summary>
    public int? ApprovedByUserId { get; set; }

    public User? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Why a manager refused it. Null unless the status is
    /// <see cref="WorkOrderStatus.Rejected"/>; requiring one on a rejection is a business
    /// rule and lives in C#, not in a DataAnnotation that cannot see the status.
    /// </summary>
    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    /// <summary>
    /// When the work finished. Distinct from <see cref="UpdatedAt"/>, which AppDbContext
    /// stamps on every write — these would stop agreeing the moment a completed order is
    /// touched again for any reason.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When the work is booked in. A collection rather than a pair of columns on this row
    /// because a job can legitimately take more than one visit, and because rescheduling
    /// should leave a trail rather than overwrite the only copy of when it was meant to
    /// happen.
    /// </summary>
    public ICollection<ScheduledSlot> ScheduledSlots { get; set; } = new List<ScheduledSlot>();
}
