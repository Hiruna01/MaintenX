using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// The reporter's answer to one <see cref="ClarificationQuestion"/>.
///
/// At most one per question — a one-to-one, not a collection. There is no thread of
/// replies here because there is no conversation: the agent asks one round of bounded
/// questions, each gets one answer, and that is the end of it.
/// </summary>
public class ClarificationAnswer
{
    public int Id { get; set; }

    public int ClarificationQuestionId { get; set; }

    public ClarificationQuestion? Question { get; set; }

    /// <summary>
    /// The answer, always as text, whatever control produced it — "Yes", the chosen
    /// option's exact string, or the short free text. One column rather than three
    /// because the question already says how to read it, and 100 characters is the
    /// agent's own cap on a short_text answer.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>
    /// Who answered. Taken from the caller's JWT `sub` claim when an endpoint to submit
    /// answers lands, never from a request body — the same rule as Report.ReporterId.
    /// </summary>
    public int AnsweredByUserId { get; set; }

    public User? AnsweredBy { get; set; }

    /// <summary>
    /// When the reporter answered. Distinct from <see cref="CreatedAt"/>, which is stamped
    /// by AppDbContext and says when the row was written; these agree today and would stop
    /// agreeing the moment answers are ever imported or backfilled.
    /// </summary>
    public DateTime AnsweredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
