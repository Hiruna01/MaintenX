using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO. Entities are never returned from a controller directly.
///
/// This is what a client needs to RENDER ONE FORM FIELD, which is why AnswerType and
/// Options are on it: the client reads AnswerType by name and draws a toggle, a picker
/// over Options, or a capped text box. There is no shape here that a message thread could
/// be built from, deliberately — see AnswerType.
/// </summary>
public record ClarificationQuestionDto(
    int Id,
    int ReportId,
    int WorkflowId,
    string QuestionText,
    AnswerType AnswerType,

    // The choices, already parsed out of the OptionsJson column, so a client never has to
    // parse a string that came out of another string. Null for every answer type except
    // SingleSelect.
    IReadOnlyList<string>? Options,

    int DisplayOrder,

    // Null while unanswered, which is how a client knows which fields are still open.
    string? AnswerText,

    DateTime? AnsweredAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
