using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IClarificationService
{
    /// <summary>
    /// Writes the clarifier's questions as ClarificationQuestion rows against a report and
    /// moves that report to AwaitingClarification.
    ///
    /// This does NOT replace the AgentStep the runner records. That row is the audit trail
    /// — what the agent produced, verbatim, at the time it produced it — and these rows
    /// are the working data the application reads, orders and answers. Both are written
    /// for every run that produces questions.
    ///
    /// Returns the number of rows written. Zero when there was nothing to write, and zero
    /// when the report or workflow does not exist — a background caller has no request to
    /// surface a 404 on, so a missing parent is a logged no-op rather than an exception.
    /// </summary>
    Task<int> RecordQuestionsAsync(
        int reportId,
        int workflowId,
        IReadOnlyList<ParsedClarifyingQuestion> questions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The questions asked about a report, in DisplayOrder, each carrying its answer if one
    /// has been given. Empty when the report has never been clarified — which is not the
    /// same as the report not existing, so callers that need to tell those apart check the
    /// report first.
    /// </summary>
    Task<IReadOnlyList<ClarificationQuestionDto>> GetForReportAsync(
        int reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the reporter's answers to a report's clarification questions — the WHOLE
    /// FORM, IN ONE REQUEST — and moves the report and its workflow on.
    ///
    /// EVERY CHECK BELOW IS MADE HERE, IN C#, AND NOT ONE OF THEM IS DELEGATED TO THE
    /// AGENT. Who may answer, whether the report is at a point where an answer means
    /// anything, whether a question belongs to this report, whether the form is complete
    /// and whether a chosen option was ever offered are all deterministic business rules,
    /// and PROJECT RULES puts those in C# where a person can read them. A model asked to
    /// adjudicate any of them would be deciding, unauditably, what the system then acts on
    /// — and the answers are the very text the agent goes on to read, so it must not also
    /// be the thing that says they are valid.
    ///
    /// The checks run in a fixed order, and the order is part of the contract: identity
    /// before state, state before content. A caller who is not the reporter learns only
    /// that, and never anything about which questions the report is carrying.
    ///
    ///   1. no such report                                  ReportNotFound
    ///   2. caller is not the original reporter             NotTheReporter
    ///   3. report is not AwaitingClarification             NotAwaitingClarification
    ///   4. a question id not belonging to this report      UnknownQuestion
    ///   5. a question left unanswered                      MissingAnswer
    ///   6. a SingleSelect answer not among its options     OptionNotOffered
    ///   7. a question that has already been answered       AlreadyAnswered
    ///
    /// Check 7 is a real query, not a hope: the unique index on
    /// ClarificationAnswer.ClarificationQuestionId will NOT reject this writer, because it
    /// has already loaded each question's answer to look at it. EF resolves the required
    /// one-to-one conflict itself and the save would succeed by REPLACING the answer. See
    /// the note in AppDbContext beside that index.
    ///
    /// On success the answers are written, the report moves to Clarified and the workflow
    /// to Diagnosing in ONE SaveChanges — the same rule as RecordQuestionsAsync, so the
    /// three can never disagree — and the workflow id is re-queued for the background
    /// runner. There is no second round: the exchange is over.
    /// </summary>
    Task<SubmitAnswersResult> SubmitAnswersAsync(
        int reportId,
        int answeredByUserId,
        SubmitAnswersRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Why a <see cref="IClarificationService.SubmitAnswersAsync"/> call ended as it did.
///
/// THIS IS NOT THE Result&lt;T&gt; WRAPPER THE CONVENTIONS RULE OUT. There is no generic,
/// nothing to unwrap and no success payload — it is a plain enum naming one of eight
/// outcomes, in the same place and for the same reason as VerificationStatusFilter. It
/// exists because a single null cannot carry seven distinct failures that map onto four
/// different status codes, and the alternative — seven extra query methods for the
/// controller to call in the right order — would move the ordering rule itself into the
/// controller, which is where it least belongs.
/// </summary>
public enum SubmitAnswersOutcome
{
    /// <summary>Answers written, report Clarified, workflow re-queued. A 204.</summary>
    Success,

    /// <summary>No report has that id. A 404.</summary>
    ReportNotFound,

    /// <summary>
    /// The caller is signed in but did not file this report. A 403, never a 404: the token
    /// is valid and we know exactly who they are, which is the distinction the API keeps
    /// everywhere else.
    /// </summary>
    NotTheReporter,

    /// <summary>
    /// Nothing is waiting on an answer — the report has not been clarified, or has been
    /// already. A 409: the request is well formed and the report is simply not there.
    /// </summary>
    NotAwaitingClarification,

    /// <summary>
    /// An answer named a question that is not one of this report's. A 400 — and the reason
    /// it is not a 404 is that the report was found; the body is what is wrong.
    /// </summary>
    UnknownQuestion,

    /// <summary>
    /// A question was left out. A 400. The form goes back whole or not at all, because a
    /// partial submission would need a second round to finish and there is no second round.
    /// </summary>
    MissingAnswer,

    /// <summary>
    /// The same question was answered twice in one request. A 400, and a guard rather than
    /// one of the seven rules: without it the two rows reach the unique index and surface
    /// as a 500 for what is plainly a malformed body.
    /// </summary>
    DuplicateAnswer,

    /// <summary>
    /// A SingleSelect answer was not one of the choices that question stored. A 400. This
    /// is the check that makes AnswerType a constraint rather than a suggestion: without
    /// it a picker is a text box wearing a picker's name.
    /// </summary>
    OptionNotOffered,

    /// <summary>
    /// A question already carries an answer. A 409 — one answer, not a thread, and the
    /// first one is evidence rather than a draft.
    /// </summary>
    AlreadyAnswered
}

/// <summary>
/// The outcome, plus the question it is about when one question in particular is at fault.
/// Null for the outcomes that concern the report as a whole.
/// </summary>
public record SubmitAnswersResult(SubmitAnswersOutcome Outcome, int? QuestionId = null);
