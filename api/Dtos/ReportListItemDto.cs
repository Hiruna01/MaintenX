using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for a list of reports — one row of a table, and nothing that would make
/// rendering that table cost a query per row. No clarification questions and no nested
/// asset; ReportDetailDto carries those.
///
/// RoomName is denormalised onto the row rather than nested as a RoomDto because a list
/// shows the room's name and nothing else about it, and UnansweredQuestionCount is a count
/// for the same reason: it is the one thing a list needs to say about clarification —
/// "this report is waiting on you" — without fetching the questions themselves.
///
/// Verification is the same idea for the other end of the lifecycle. A report's own status
/// stops at WorkOrderRaised or Closed, and neither says whether the repair HELD — so a
/// reporter who answered "no, still broken" would see nothing on their report change. The
/// latest check's state travels on the row instead, so the report says what happened next.
/// </summary>
public record ReportListItemDto(
    int Id,
    int ReporterId,
    int RoomId,
    string RoomName,
    int? AssetId,
    string Description,
    ReportStatus Status,
    int UnansweredQuestionCount,

    // The newest verification check on a work order raised for this report. NULL when there
    // is none — no repair has been completed yet — which is not the same as an open one.
    ReportVerificationDto? Verification,

    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Where the check on a report's repair has got to, as a report list row shows it. The id is
/// what a reporter's client opens to answer it.
///
/// AgentOutcome is the verification agent's label (confirm / reopen / escalate) as a STRING,
/// the same as VerificationCheck.AgentOutcome: an opinion shown beside Status, which is set
/// in C# from the reporter's answer and never from this.
/// </summary>
public record ReportVerificationDto(
    int Id,
    VerificationStatus Status,
    string? AgentOutcome);
