using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — a manager's decision on a work order that crossed the approval threshold.
///
/// No ApprovedByUserId: who decided comes from the caller's JWT `sub` claim, never from
/// the request body, the same rule as CreateReportDto's missing ReporterId. A client that
/// could name the approver could approve its own spending as somebody else.
///
/// One record for both outcomes rather than two endpoints, because they are one decision
/// with one audit trail. That a rejection MUST carry a reason is a rule spanning both
/// fields — a DataAnnotation on RejectionReason cannot see Approved — so it is enforced in
/// the service in C#, the same way an answer is checked against its own question's type.
/// </summary>
public record ApprovalDecisionDto(
    bool Approved,

    [MaxLength(1000)] string? RejectionReason);
