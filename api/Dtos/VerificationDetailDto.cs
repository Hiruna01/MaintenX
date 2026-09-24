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

    bool? ReporterConfirmed,
    string? ReporterComment,
    DateTime? ReporterRespondedAt,

    // The agent's own label and reasoning, shown as an opinion beside the reporter's
    // answer rather than in place of it. Status is never set from these — see
    // VerificationCheck.AgentOutcome.
    string? AgentOutcome,
    string? AgentReason,

    DateTime? ProcessedAt,

    // When the sweep handed it to the agent, and — only when Expired — why it gave up.
    DateTime? AgentQueuedAt,
    string? ExpiredReason,

    DateTime CreatedAt,
    DateTime UpdatedAt);
