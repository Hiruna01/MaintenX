namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — the approval gate's own account of a work order: the threshold, which side
/// of it the estimate sits on, and whether the strategy needs a manager whatever it costs.
///
/// EVERY FIELD IS COMPUTED IN C#, by the same function WorkOrderService.CreateAsync routes
/// with, so the reason a page gives for an order needing approval cannot disagree with the
/// reason it actually does. A client renders "Rs 45,000 — above the Rs 15,000 approval
/// threshold" from these fields and compares nothing itself: a second copy of the rule in
/// JavaScript would be a second answer waiting to disagree.
///
/// The threshold is the one configured NOW (Approval:CostThreshold), not the one in force
/// when the order was raised — a threshold is not stored per order. For an order awaiting a
/// decision, which is what this is read for, those are the same number.
/// </summary>
public record ApprovalBasisDto(
    // decimal, like the estimate it is compared with — see ApprovalSettings.
    decimal Threshold,

    // Strictly above. An estimate exactly on the threshold does not need a manager.
    bool ExceedsThreshold,

    // EscalateReplacement always needs a manager, however cheap.
    bool IsReplacement,

    // ExceedsThreshold || IsReplacement — carried rather than left to the client to OR
    // together, so there is exactly one place the rule is written.
    bool RequiresApproval);
