using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// One report with everything a reader needs about it: where it is, what equipment it
/// blames, and the clarification questions asked about it in DisplayOrder with whatever
/// answers have come back.
///
/// The questions are on the DETAIL DTO and not the list one for the same reason the asset
/// registry keeps its service history off AssetDto: a list of reports would otherwise pull
/// every question for every row to render something no list shows.
///
/// Room and Asset are resolved objects rather than ids because the reader of a single
/// report is a human looking at a screen, and "MAB101" is an answer where "17" is another
/// request. Asset is null when the report names none.
/// </summary>
public record ReportDetailDto(
    int Id,
    int ReporterId,
    RoomDto Room,
    AssetDto? Asset,
    string Description,
    ReportStatus Status,
    string? PhotoUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ClarificationQuestionDto> ClarificationQuestions);
