using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a list of verification checks — one row of a table, and nothing that
/// would make rendering that table cost a query per row. No nested asset and no agent
/// reasoning; VerificationDetailDto carries those.
///
/// AssetTag is denormalised onto the row for the same reason ReportListItemDto carries
/// RoomName: a list shows the sticker on the machine, not an object describing it.
///
/// ReportDescription and WorkOrderCompletedAt are denormalised for the reporter's side of the
/// same list: a reporter is not expected to know an asset tag (see Report.AssetId), so a row
/// that said only "PRJ-MAB101-01" would not tell them which of their faults they are being
/// asked about. What they reported and when it was repaired does.
/// </summary>
public record VerificationCheckDto(
    int Id,
    int WorkOrderId,

    // The report whose fault the repair was for, and its description verbatim.
    int ReportId,
    string ReportDescription,

    // When the technician claimed the repair — the "since" the question is about.
    DateTime? WorkOrderCompletedAt,

    int AssetId,
    string AssetTag,
    DateTime DueAt,
    VerificationStatus Status,

    // Null while unanswered, which is how a client knows which checks are still open —
    // and why this is a bool? here as well as on the entity.
    bool? ReporterConfirmed,

    // Past a deadline nothing has met: Pending with DueAt passed (the sweep has not asked
    // yet), or AwaitingReporterResponse longer than the response window (the reporter has
    // not answered). Decided in VerificationService.IsOverdue, in C#; a client only colours
    // it and never compares dates itself.
    bool IsOverdue,

    DateTime? ReporterRespondedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
