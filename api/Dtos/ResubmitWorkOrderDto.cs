using System.ComponentModel.DataAnnotations;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — a Draft that was sent back for revision, re-planned and put through the
/// approval gate again. The SAME ORDER: no Id, ReportId or AssetId, because the order already
/// has all three and a revision does not move the job to other equipment.
///
/// The same three fields a manager sets when raising one, bounded exactly as
/// <see cref="CreateWorkOrderDto"/> bounds them — and, like it, no Status: where the order
/// lands is ApprovalBasisFor comparing this estimate with the threshold, never the caller.
/// </summary>
public record ResubmitWorkOrderDto(
    // Nullable with [Required], for the reason CreateWorkOrderDto gives: a plain enum binds a
    // missing field as KnownFix, and the gate would route a strategy nobody stated.
    [Required] WorkOrderStrategy? Strategy,

    // Nullable with [Required], and the decimal overload of [Range]: a plain decimal binds a
    // missing estimate as 0, which is under any threshold, and the order would be approved
    // without anyone having costed it.
    [Required]
    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true)]
    decimal? EstimatedCost,

    [MaxLength(1000)] string? PartsRequired);
