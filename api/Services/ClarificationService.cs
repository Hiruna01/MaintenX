using System.Text.Json;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class ClarificationService : IClarificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ClarificationService> _logger;

    public ClarificationService(AppDbContext db, ILogger<ClarificationService> logger)
    {
        _db = db;
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
