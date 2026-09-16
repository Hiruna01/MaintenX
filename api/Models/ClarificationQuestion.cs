using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// One question the clarifier agent put to the reporter about a report.
///
/// This is the WORKING DATA, not the audit trail. The same questions are also written
/// verbatim into <see cref="AgentStep.PayloadJson"/> by the background runner, and that
/// row stays: it records what the agent produced, at the time it produced it, and must
/// never be edited. These rows are the copy the application actually uses — they are
/// queried per report, ordered, rendered as a form, and answered.
/// </summary>
public class ClarificationQuestion
{
    public int Id { get; set; }

    /// <summary>
    /// The report being clarified. Not nullable: a question that is not about a report has
    /// nobody to ask and nowhere to appear. A workflow started from a bare objective
    /// (AgentWorkflow.ReportId is null) therefore produces no rows here — only its
    /// AgentStep — which is why the runner checks before persisting.
    /// </summary>
    public int ReportId { get; set; }

    public Report? Report { get; set; }

    /// <summary>
    /// The workflow run that produced this question. Kept alongside ReportId rather than
    /// derived from it: a report may be clarified by more than one run over its life, and
    /// this is what says which run asked what.
    /// </summary>
    public int WorkflowId { get; set; }

    public AgentWorkflow? Workflow { get; set; }

    /// <summary>
    /// The question as the reporter reads it. 300 characters, matching the agent's own
    /// ClarifyingQuestion.question_text cap, so a reply that validated there cannot fail
    /// to store here.
    /// </summary>
    [Required]
    [MaxLength(300)]
    public string QuestionText { get; set; } = string.Empty;

    public AnswerType AnswerType { get; set; }

    /// <summary>
    /// The choices for a <see cref="AnswerType.SingleSelect"/> question, as a JSON array of
    /// strings. PostgreSQL jsonb, not text, same as the other JSON columns.
    ///
    /// Null for every other answer type, and that is a rule rather than a habit: a yes/no
    /// or short-text question carrying options would render as a control the agent did not
    /// ask for. The agent's own schema refuses that shape, and the parser in
    /// AgentRunResponse drops options for those types rather than trusting them through.
    /// </summary>
    public string? OptionsJson { get; set; }

    /// <summary>
    /// Where this question sits in the form, 0-based. Stored rather than inferred from Id,
    /// because the order the agent asked in is part of the question set and Id only
    /// happens to agree with it today.
    /// </summary>
    public int DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The one answer, or null while the question is unanswered. At most one, enforced by
    /// a unique index on ClarificationAnswer.ClarificationQuestionId (see AppDbContext) —
    /// a second answer to the same question is a database error, not a silent overwrite.
    /// </summary>
    public ClarificationAnswer? Answer { get; set; }
}
