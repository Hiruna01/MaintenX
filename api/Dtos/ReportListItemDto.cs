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
    DateTime CreatedAt,
    DateTime UpdatedAt);
