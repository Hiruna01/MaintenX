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
}
