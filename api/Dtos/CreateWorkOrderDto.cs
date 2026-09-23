using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO. No Id — the server assigns it.
///
/// No Status either: every work order is raised as <see cref="WorkOrderStatus.Draft"/>,
/// and whether it then needs a manager's decision is decided by comparing EstimatedCost
/// against the configured threshold in C# (see ApprovalSettings). Letting a caller post a
/// status would let one post "Approved" and skip that comparison entirely, which is the
/// approval control removed by the client that is supposed to be subject to it.
///
/// No AssignedTechnicianId, ActualCost, ApprovedByUserId or ResolutionNote for the same
/// family of reasons — each belongs to a later step with its own DTO, and none of them is
/// something the raiser of an order knows yet.
/// </summary>
public record CreateWorkOrderDto(
    [Range(1, int.MaxValue)] int ReportId,

    // Required, unlike Report.AssetId: a reporter is not expected to know which asset tag
    // the misbehaving projector carries, but nobody can be sent to repair a machine nobody
    // has identified. See WorkOrder.AssetId.
    [Range(1, int.MaxValue)] int AssetId,

    // NULLABLE WITH [Required], not a plain enum: a plain one binds a missing field as its
    // first member, KnownFix, and the approval gate would then route an order whose
    // strategy nobody stated. EscalateReplacement always needs a manager, so the strategy
    // is an input to the gate and has to be one the caller actually chose.
    [Required] WorkOrderStrategy? Strategy,

    // Bounded with the decimal overload of RangeAttribute, not [Range(0, double.MaxValue)]:
    // that overload would parse this cost through a binary double to compare it, which is
    // exactly what this project refuses to do with money. The limits are parsed invariantly
    // so the validator behaves the same under any server locale — a comma decimal
    // separator must not change what is accepted.
    //
    // The upper bound is a sanity cap, not a policy: a single campus repair costing more
    // than ten million rupees is a typo, and the real spending control is the approval
    // threshold rather than anything stated here.
    //
    // Nullable with [Required] for the same reason as Strategy, and it matters more here:
    // a plain decimal binds a missing estimate as 0, which is under any threshold, and the
    // order would be auto-approved. That would be the approval gate opened by leaving a
    // field out.
    [Required]
    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true)]
    decimal? EstimatedCost,

    [MaxLength(1000)] string? PartsRequired);
