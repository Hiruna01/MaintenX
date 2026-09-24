using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One work order with everything a reader needs about it: what machine, what fault, who
/// is going, what it costs, who approved it and when it is booked.
///
/// Asset is a resolved object rather than an id because the reader of a single work order
/// is a human looking at a screen, and "PRJ-MAB101-01 in MAB101" is an answer where "17"
/// is another request. ReportDescription is carried for the same reason and is the one
/// field of the report worth repeating here — a reader needs to know what was complained
/// about without opening the report.
///
/// The scheduled slots are on the DETAIL DTO and not the list one, the same way the asset
/// registry keeps service history off AssetDto: a list of work orders would otherwise pull
/// every slot for every row to render something no list shows.
///
/// Room and Diagnosis are here for the technician, who reads this DTO and nothing else
/// before a visit: a Technician cannot open the report (ReportService scopes it to the
/// reporter) nor the approval queue (FacilitiesManager only), and should still arrive
/// knowing where the machine is and what the agent thinks is wrong with it.
/// </summary>
public record WorkOrderDetailDto(
    int Id,
    int ReportId,
    string ReportDescription,
    AssetDto Asset,

    // Where the asset is. Resolved rather than Asset.RoomId, because "MAB101 · Lecture
    // Hall A" is where a technician walks to and "12" is not.
    RoomDto Room,

    // Null while the order is unassigned; a whole UserDto rather than a name because a
    // detail view offers to contact whoever is going.
    UserDto? AssignedTechnician,

    WorkOrderStatus Status,
    WorkOrderStrategy Strategy,

    // decimal end to end — see WorkOrder.EstimatedCost for why this is never a double.
    decimal EstimatedCost,
    decimal? ActualCost,

    // Which side of the approval gate the estimate sits on, and why — computed in C# by the
    // same function that routed the order, so a page never compares money itself.
    ApprovalBasisDto ApprovalBasis,

    string? PartsRequired,
    string? ResolutionNote,
    string? CompletionPhotoUrl,

    // All three are null on an order that never needed a decision — every order whose
    // estimate sat under the approval threshold. Null here does not mean "still waiting";
    // Status is what says that.
    UserDto? ApprovedBy,
    DateTime? ApprovedAt,
    string? RejectionReason,

    // The manager's note from the last request-revision, if there was one. Carried so the
    // order says why it went back to Draft; the Strategist reads the same column.
    string? RevisionNote,

    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,

    // Every visit booked for this order, oldest-first — a job can take more than one, and
    // a rescheduled order keeps the slot it was moved from.
    IReadOnlyList<ScheduledSlotDto> ScheduledSlots,

    // The diagnostic agent's latest answer for the report — the same reading, by the same
    // code, as ApprovalCaseDto.Diagnosis. ADVICE, never a decision. NULL IS NOT EMPTY: null
    // means no diagnosis was ever recorded; a failed run comes back with its reason.
    AgentDiagnosisDto? Diagnosis);
