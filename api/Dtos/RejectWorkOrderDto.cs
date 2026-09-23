using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — a manager refusing a work order that crossed the approval threshold.
///
/// THE REASON IS REQUIRED, and it is the whole body. A refusal with no reason is a dead end
/// for everybody downstream: the reporter's fault is still in the room, and whoever raises
/// the next order for it has nothing to go on. [Required] also refuses a whitespace-only
/// string, so a 400 comes back from model binding before the service is ever called.
///
/// No RejectedByUserId: who decided comes from the caller's JWT `sub` claim, never from the
/// body — the same rule as CreateReportDto's missing ReporterId.
/// </summary>
public record RejectWorkOrderDto(
    [Required]
    [MaxLength(1000)]
    string Reason);
