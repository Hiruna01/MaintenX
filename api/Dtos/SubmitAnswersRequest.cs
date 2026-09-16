using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — the reporter's answers to a report's clarification questions, submitted as
/// ONE round. No Id, and no AnsweredByUserId: who answered comes from the caller's JWT
/// `sub` claim, the same rule as CreateReportDto's missing ReporterId.
///
/// A LIST, SUBMITTED ONCE — not a message. The whole form goes back in a single request
/// and the exchange is over; there is no reply field, no thread id, and nothing that would
/// let a second turn follow. That is the same constraint the agent service pins with tests
/// on ClarifierOutput, held on this side of the wire.
/// </summary>
public record SubmitAnswersRequest(
    [Required]
    [MinLength(1)]
    IReadOnlyList<SubmittedAnswer> Answers);

/// <summary>
/// One answer in a <see cref="SubmitAnswersRequest"/>. QuestionId names an existing
/// ClarificationQuestion — it is not this record's own id, which is why it is present at
/// all.
///
/// AnswerText is capped at 100 characters to match ClarificationAnswer.AnswerText and the
/// agent's own short_text cap. The validation that the text actually FITS ITS QUESTION —
/// a yes/no answering "Yes" or "No", a single-select answering one of that question's
/// stored options — is a deterministic business rule and belongs in the service in C#,
/// not in a DataAnnotation that cannot see the question.
/// </summary>
public record SubmittedAnswer(
    [Range(1, int.MaxValue)] int QuestionId,

    [Required]
    [StringLength(100, MinimumLength = 1)]
    string AnswerText);
