using System.Text.Json;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class ClarificationService : IClarificationService
{
    /// <summary>
    /// The only two answers a YesNo question takes — exactly the strings both clients send.
    /// Fixed here rather than stored per question, because the agent never supplies options
    /// for a yes/no question and a toggle has nothing else to offer.
    /// </summary>
    public static readonly IReadOnlyList<string> YesNoOptions = new[] { "Yes", "No" };

    private readonly AppDbContext _db;

    // The same hand-off ReportService uses. Answering the form is what lets the run carry
    // on, so this service re-queues the workflow rather than leaving the controller to do
    // it — a controller's job is one call and a status code.
    private readonly IWorkflowQueue _workflowQueue;

    private readonly ILogger<ClarificationService> _logger;

    public ClarificationService(
        AppDbContext db,
        IWorkflowQueue workflowQueue,
        ILogger<ClarificationService> logger)
    {
        _db = db;
        _workflowQueue = workflowQueue;
        _logger = logger;
    }

    public async Task<int> RecordQuestionsAsync(
        int reportId,
        int workflowId,
        IReadOnlyList<ParsedClarifyingQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        if (questions.Count == 0)
        {
            // Nothing needed clarifying. A valid, meaningful answer — and not a reason to
            // move the report anywhere, because nobody is being waited on.
            return 0;
        }

        // Both are real foreign keys, so a missing parent would otherwise surface as a
        // constraint violation out of the driver — inside a background worker, where
        // there is no request to turn it into a status code.
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            _logger.LogWarning(
                "Cannot record clarification questions: report {ReportId} does not exist.", reportId);
            return 0;
        }

        if (!await _db.AgentWorkflows.AnyAsync(w => w.Id == workflowId, cancellationToken))
        {
            _logger.LogWarning(
                "Cannot record clarification questions: workflow {WorkflowId} does not exist.",
                workflowId);
            return 0;
        }

        foreach (var question in questions)
        {
            _db.ClarificationQuestions.Add(new ClarificationQuestion
            {
                ReportId = reportId,
                WorkflowId = workflowId,
                QuestionText = question.QuestionText,
                AnswerType = question.AnswerType,
                OptionsJson = question.OptionsJson,
                DisplayOrder = question.DisplayOrder
            });
        }

        // The report is now waiting on its reporter, so it says so. This is a deterministic
        // business rule and therefore lives in C# — the agent decides WHAT to ask, never
        // what that means for the report's status.
        //
        // It is set in the same SaveChanges as the rows above so the two cannot disagree:
        // a report is never left saying AwaitingClarification with no questions to answer,
        // and never left saying Submitted with questions sitting against it.
        report.Status = ReportStatus.AwaitingClarification;

        await _db.SaveChangesAsync(cancellationToken);

        return questions.Count;
    }

    public async Task<IReadOnlyList<ClarificationQuestionDto>> GetForReportAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        var questions = await _db.ClarificationQuestions
            .AsNoTracking()
            .Include(q => q.Answer)
            .Where(q => q.ReportId == reportId)
            // DisplayOrder first, because that is the order the agent asked in and the
            // order the form renders. Id breaks the tie when more than one run has
            // clarified the same report, keeping each run's questions together.
            .OrderBy(q => q.DisplayOrder)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

        return questions.Select(ToDto).ToList();
    }

    public async Task<SubmitAnswersResult> SubmitAnswersAsync(
        int reportId,
        int answeredByUserId,
        SubmitAnswersRequest request,
        CancellationToken cancellationToken = default)
    {
        // ------------------------------------------------------------------------------
        // NONE OF THE CHECKS IN THIS METHOD MAY BE DELEGATED TO THE AGENT, AND THE ORDER
        // THEY RUN IN IS PART OF THE CONTRACT.
        //
        // Who is allowed to answer, whether the report is at a point where an answer means
        // anything, whether a question belongs to this report, whether the form came back
        // whole and whether a chosen option was ever offered are every one of them a
        // deterministic business rule. PROJECT RULES puts those in C#, never in a prompt:
        // a model adjudicating them would be deciding unauditably what the system then
        // acts on, and it would be doing so over the very text it goes on to read. The
        // agent decides WHAT TO ASK. Everything about what comes back is decided here.
        // ------------------------------------------------------------------------------

        // 1. No such report. Checked first because nothing below means anything without it.
        var report = await _db.Reports
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return new SubmitAnswersResult(SubmitAnswersOutcome.ReportNotFound);
        }

        // 2. Not the original reporter. Identity before state, so a stranger is told only
        //    that this is not theirs and learns nothing about where the report has got to.
        //
        //    answeredByUserId is a parameter rather than a field on SubmitAnswersRequest
        //    because it comes from the caller's token — the same rule that keeps
        //    ReporterId off CreateReportDto. A client cannot answer as somebody else.
        if (report.ReporterId != answeredByUserId)
        {
            return new SubmitAnswersResult(SubmitAnswersOutcome.NotTheReporter);
        }

        // 3. Nothing is waiting on an answer. A report that was never clarified, or that
        //    has been answered already and moved on, is not a validation failure — the
        //    body may be perfectly well formed — so this is a conflict, not a 400.
        if (report.Status != ReportStatus.AwaitingClarification)
        {
            return new SubmitAnswersResult(SubmitAnswersOutcome.NotAwaitingClarification);
        }

        // Every question this report is carrying, in the order the form renders them.
        //
        // Include(q => q.Answer) is load-bearing twice over: check 7 reads it, and without
        // it a re-answer would reach the unique index as a database error rather than as
        // the 409 this method owes the caller. See AppDbContext beside that index.
        var questions = await _db.ClarificationQuestions
            .Include(q => q.Answer)
            .Where(q => q.ReportId == reportId)
            .OrderBy(q => q.DisplayOrder)
            .ThenBy(q => q.Id)
            .ToListAsync(cancellationToken);

        var questionsById = questions.ToDictionary(q => q.Id);

        // 4. A question id that does not belong to this report. Not a 404 — the report was
        //    found, and it is the body that is wrong — and deliberately not a silent skip:
        //    an answer quietly dropped would leave the reporter believing they had
        //    answered something they had not.
        foreach (var answer in request.Answers)
        {
            if (!questionsById.ContainsKey(answer.QuestionId))
            {
                return new SubmitAnswersResult(
                    SubmitAnswersOutcome.UnknownQuestion, answer.QuestionId);
            }
        }

        // A guard rather than one of the seven rules: two answers to one question would
        // otherwise reach the unique index and surface as a 500 for a malformed body.
        var submitted = new Dictionary<int, SubmittedAnswer>();

        foreach (var answer in request.Answers)
        {
            if (!submitted.TryAdd(answer.QuestionId, answer))
            {
                return new SubmitAnswersResult(
                    SubmitAnswersOutcome.DuplicateAnswer, answer.QuestionId);
            }
        }

        // 5. A question left unanswered. The whole form comes back or none of it does:
        //    accepting a partial submission would need a second round to finish it, and a
        //    second round is the conversation this system does not have.
        foreach (var question in questions)
        {
            if (!submitted.ContainsKey(question.Id))
            {
                return new SubmitAnswersResult(
                    SubmitAnswersOutcome.MissingAnswer, question.Id);
            }
        }

        // 6. A SingleSelect or YesNo answer that is not one of the choices that question
        //    offers.
        //
        //    This is what makes AnswerType a constraint instead of a suggestion: a picker
        //    whose value is never checked against its own options is a text box wearing a
        //    picker's name, and unbounded text is the thing AnswerType exists to prevent.
        //    A yes/no toggle is the same picker with its two options fixed here rather than
        //    stored per question — without this, "Yes" was a convention the clients kept
        //    and any 100 characters would have been accepted in its place.
        //
        //    Matched exactly, ordinally: the client was handed these strings verbatim by
        //    GetForReportAsync, so it has one to send back unchanged. Anything looser
        //    would be guessing at what the reporter meant, which is not this layer's job.
        foreach (var question in questions.Where(q => q.AnswerType != AnswerType.ShortText))
        {
            var options = question.AnswerType == AnswerType.YesNo
                ? YesNoOptions
                : ParseOptions(question.OptionsJson);
            var answerText = submitted[question.Id].AnswerText;

            if (options is null || !options.Contains(answerText, StringComparer.Ordinal))
            {
                return new SubmitAnswersResult(
                    SubmitAnswersOutcome.OptionNotOffered, question.Id);
            }
        }

        // 7. Already answered. LAST, so a re-submission is told what is actually wrong
        //    with it rather than being sent away over a typo it also contained.
        //
        //    This has to be an explicit query. The unique index on
        //    ClarificationAnswer.ClarificationQuestionId will NOT reject this writer: it
        //    has loaded the existing answers above, so EF resolves the required one-to-one
        //    conflict itself and the save would SUCCEED by replacing them — overwriting the
        //    evidence of what the reporter first said. The index only stops a writer that
        //    knows nothing but a question id.
        var alreadyAnswered = questions.FirstOrDefault(q => q.Answer is not null);

        if (alreadyAnswered is not null)
        {
            return new SubmitAnswersResult(
                SubmitAnswersOutcome.AlreadyAnswered, alreadyAnswered.Id);
        }

        // One timestamp for the lot: they were submitted together, in one request, and
        // recording them fractions of a millisecond apart would imply an order that the
        // form does not have.
        var answeredAt = DateTime.UtcNow;

        foreach (var question in questions)
        {
            _db.ClarificationAnswers.Add(new ClarificationAnswer
            {
                ClarificationQuestionId = question.Id,
                AnswerText = submitted[question.Id].AnswerText,
                // From the token, never from the body — see the parameter above.
                AnsweredByUserId = answeredByUserId,
                AnsweredAt = answeredAt
            });
        }

        // The fault is no longer waiting on its reporter. Deterministic business rule,
        // therefore C#, and set in the SAME SaveChanges as the answer rows for the same
        // reason RecordQuestionsAsync sets AwaitingClarification alongside its questions:
        // a report must never be left saying Clarified with nothing answered against it,
        // nor AwaitingClarification with a complete set of answers sitting there.
        report.Status = ReportStatus.Clarified;

        // The run that asked is the run that gets the answers. Taken from the questions
        // rather than from the report, because a report may be clarified by more than one
        // run over its life and only these rows say which run asked what; the highest id
        // is the most recent run, which is the one that is waiting.
        var workflowId = questions.Max(q => q.WorkflowId);

        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null)
        {
            // WorkflowId is a real foreign key and deletes are Restrict, so this cannot
            // happen. Logged rather than thrown: the answers are worth keeping either way.
            _logger.LogWarning(
                "Report {ReportId} was clarified but workflow {WorkflowId} is missing, so its "
                + "state was not advanced.", reportId, workflowId);
        }
        else
        {
            // The clarification is over, so the run goes back to what it was doing. Which
            // state follows which is a business rule and lives in C#, never in a prompt —
            // WorkflowTransitions, which refuses (409) a workflow that is not waiting here.
            WorkflowTransitions.Move(workflow, WorkflowTrigger.ReporterAnswered);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // AFTER the save, never before: the runner opens its own DI scope and would find
        // no answers if the rows were not committed yet.
        //
        // CancellationToken.None, not the request's — that token is cancelled the moment
        // the response is written, which would abort the hand-off we just promised. Same
        // reasoning as ReportService.CreateAsync.
        //
        // The runner picks it up in Diagnosing and resumes the run with these answers, which
        // is what sends graph.py straight to the diagnostic without asking again.
        await _workflowQueue.EnqueueAsync(workflowId, CancellationToken.None);

        return new SubmitAnswersResult(SubmitAnswersOutcome.Success);
    }

    private static ClarificationQuestionDto ToDto(ClarificationQuestion q) =>
        new(q.Id,
            q.ReportId,
            q.WorkflowId,
            q.QuestionText,
            q.AnswerType,
            ParseOptions(q.OptionsJson),
            q.DisplayOrder,
            q.Answer?.AnswerText,
            q.Answer?.AnsweredAt,
            q.CreatedAt,
            q.UpdatedAt);

    /// <summary>
    /// Turns the stored jsonb array back into a list, so a client is handed options rather
    /// than a string it has to parse itself.
    ///
    /// Deserialisation is guarded even though this column is only ever written by
    /// ParseQuestions: the value is a string as far as C# is concerned, and a read path
    /// that throws on one malformed row would take out the whole report page with it.
    /// </summary>
    private static IReadOnlyList<string>? ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(optionsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
