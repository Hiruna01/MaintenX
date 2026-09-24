using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One verification check with everything a reader needs: which machine, what the
/// technician said they did, what the reporter said afterwards, and what the agent made
/// of the two.
///
/// Asset is a resolved object rather than an id because the reader of a single check is a
/// human looking at a screen. WorkOrderResolutionNote and WorkOrderCompletedAt are carried
/// across from the work order because the whole question being asked is "did THAT hold?" —
/// the answer is unreadable without the claim it is answering.
///
/// The work order is carried as those fields rather than as a WorkOrderDto because a
/// Reporter reads this too: they are shown what was done and when, not the estimate, the
/// cost or who was sent — a Reporter can read no work order through WorkOrdersController.
/// </summary>
public record VerificationDetailDto(
    int Id,
    int WorkOrderId,

    // The report whose fault the repair was for — the reporter's own way into this check,
    // since a Reporter cannot read the work order itself.
    int ReportId,

    AssetDto Asset,

    // What the technician recorded on completion, and when. Null resolution note is
    // possible on older work orders; CompleteWorkOrderDto requires one going forward.
    string? WorkOrderResolutionNote,
    DateTime? WorkOrderCompletedAt,

    DateTime DueAt,
    VerificationStatus Status,

    // The same rule, from the same method, as VerificationCheckDto.IsOverdue.
    bool IsOverdue,

    bool? ReporterConfirmed,
    string? ReporterComment,
    DateTime? ReporterRespondedAt,

    // The agent's own label and reasoning, shown as an opinion beside the reporter's
    // answer rather than in place of it. Status is never set from these — see
    // VerificationCheck.AgentOutcome.
    string? AgentOutcome,
    string? AgentReason,

    // The evidence the agent cited, verbatim, one item per string. Null when the agent has
    // not judged the check — never an empty list standing in for "not judged".
    IReadOnlyList<string>? AgentEvidence,

    // Reports filed against the same asset AFTER this repair was completed, the original
    // report excluded, oldest first. Closed ones included: a report closed as a duplicate
    // is still somebody seeing the fault again.
    //
    // NULL for a caller who does not see every check. These are other people's reports,
    // and a Reporter reads the reports they filed and nobody else's — the same rule as
    // ReportService. Null is not empty: an empty list means nobody has reported it since.
    IReadOnlyList<VerificationRelatedReportDto>? NewReportsSinceCompletion,

    // Work orders raised on the same asset AFTER this repair was completed, oldest first —
    // what a reopened or escalated fault looped back to. Null for a non-manager for the
    // same reason as above (a Reporter reads no work orders), and an empty list means no
    // follow-up has been raised yet.
    IReadOnlyList<VerificationFollowUpDto>? FollowUpWorkOrders,

    DateTime? ProcessedAt,

    // When the sweep handed it to the agent, and — only when Expired — why it gave up.
    DateTime? AgentQueuedAt,
    string? ExpiredReason,

    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>A report filed on the check's asset since the repair. See VerificationDetailDto.</summary>
public record VerificationRelatedReportDto(
    int Id,
    string Description,
    ReportStatus Status,
    DateTime CreatedAt);

/// <summary>A work order raised on the check's asset since the repair. See VerificationDetailDto.</summary>
public record VerificationFollowUpDto(
    int Id,
    WorkOrderStatus Status,
    WorkOrderStrategy Strategy,
    DateTime CreatedAt);
