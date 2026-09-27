using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class VerificationAgentService : IVerificationAgentService
{
    /// <summary>
    /// One pass at a time. The timer and a wake can both start one, and two passes over the same
    /// rows would call the agent twice for one check. Static because the service is scoped —
    /// same as VerificationService's sweep lock.
    /// </summary>
    private static readonly SemaphoreSlim PassLock = new(1, 1);

    // Column limits, so a long value is cut rather than losing the whole save.
    private const int MaxOutcomeLength = 100;
    private const int MaxReasonLength = 2000;
    private const int MaxAgentErrorLength = 500;
    private const int MaxStepErrorLength = 2000;

    /// <summary>The agent's bound on reporter_comment, and ReporterConfirmationDto's.</summary>
    private const int MaxReporterCommentLength = 300;

    private readonly AppDbContext _db;
    private readonly IAgentClient _agent;
    private readonly TimeProvider _time;
    private readonly ILogger<VerificationAgentService> _logger;

    public VerificationAgentService(
        AppDbContext db,
        IAgentClient agent,
        TimeProvider time,
        ILogger<VerificationAgentService> logger)
    {
        _db = db;
        _agent = agent;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private enum CheckOutcome
    {
        Judged,
        Retrying,
        GaveUp,

        /// <summary>Gone, no longer waiting, or queued again mid-run. Not counted.</summary>
        Skipped
    }

    public async Task<VerificationAgentPassResult> JudgeQueuedChecksAsync(CancellationToken cancellationToken = default)
    {
        await PassLock.WaitAsync(cancellationToken);

        try
        {
            // Ids only, oldest queued first: each row is loaded, run and saved on its own below.
            var ids = await _db.VerificationChecks
                .AsNoTracking()
                .Where(VerificationAgentRules.AwaitingJudgement)
                .OrderBy(v => v.AgentQueuedAt)
                .ThenBy(v => v.Id)
                .Select(v => v.Id)
                .ToListAsync(cancellationToken);

            int judged = 0, retrying = 0, gaveUp = 0, failed = 0;

            foreach (var id in ids)
            {
                try
                {
                    switch (await JudgeOneAsync(id, cancellationToken))
                    {
                        case CheckOutcome.Judged: judged++; break;
                        case CheckOutcome.Retrying: retrying++; break;
                        case CheckOutcome.GaveUp: gaveUp++; break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Left waiting for the next pass. Its attempt, if one was spent, is already
                    // counted, so a row that throws every time still reaches the bound.
                    _logger.LogError(ex, "Verification agent run failed on check {CheckId}.", id);

                    // Or the failed change rides along with the next row's save.
                    _db.ChangeTracker.Clear();
                    failed++;
                }
            }

            if (ids.Count > 0)
            {
                _logger.LogInformation(
                    "Verification agent pass: {Judged} judged, {Retrying} to retry, {GaveUp} given up, {Failed} failed.",
                    judged, retrying, gaveUp, failed);
            }

            return new VerificationAgentPassResult(judged, retrying, gaveUp, failed);
        }
        finally
        {
            PassLock.Release();
        }
    }

    private async Task<CheckOutcome> JudgeOneAsync(int id, CancellationToken cancellationToken)
    {
        var check = await _db.VerificationChecks
            .Include(v => v.WorkOrder)
                .ThenInclude(w => w!.Report)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        // Re-read, not assumed: the row may have been judged since the ids were listed.
        if (check is null
            || VerificationAgentRules.StateOf(check) is not (VerificationAgentState.Queued or VerificationAgentState.Retrying))
        {
            return CheckOutcome.Skipped;
        }

        // A row whose attempts were all spent without an answer — the process died mid-call on
        // the last one — is given up on here, without another call.
        if (check.AgentAttempts >= VerificationAgentRules.MaxAttempts)
        {
            GiveUp(check, $"Gave up after {check.AgentAttempts} attempts. {check.AgentError}".Trim());
            await _db.SaveChangesAsync(cancellationToken);
            return CheckOutcome.GaveUp;
        }

        var report = check.WorkOrder?.Report;

        // THE RUN'S WORKFLOW: the latest one raised for the report — the one the repair came
        // out of, or the reopened run after it — so the verdict lands on the same audit trail
        // as the repair it judges. The same "latest" the reporter's answer moves.
        int? workflowId = report is null
            ? null
            : await _db.AgentWorkflows
                .Where(w => w.ReportId == report.Id)
                .OrderByDescending(w => w.Id)
                .Select(w => (int?)w.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (report is null || workflowId is null)
        {
            // An AgentStep needs a workflow, and so does every tool call the agent would make —
            // it could not read the repair it was asked to judge. Not retried: nothing will
            // change by the next pass.
            _logger.LogWarning(
                "Verification check {CheckId}: report {ReportId} has no agent workflow, so the verification agent was not asked.",
                check.Id, report?.Id);
            GiveUp(check, "The report has no agent workflow to record a run against, so the verification agent was not asked.");
            await _db.SaveChangesAsync(cancellationToken);
            return CheckOutcome.GaveUp;
        }

        var queuedStamp = check.AgentQueuedAt;

        // Counted BEFORE the call and saved, so a process that dies mid-call has still spent it
        // and a row can never be retried without bound.
        check.AgentAttempts++;
        await _db.SaveChangesAsync(cancellationToken);

        var call = await _agent.RunAsync(
            new AgentRunRequest(
                WorkflowId: workflowId.Value,
                Description: report.Description,
                RoomId: null,
                BuildingId: null,
                Verification: new AgentVerificationRequest(
                    check.WorkOrderId,
                    check.ReporterConfirmed,
                    CommentForAgent(check.ReporterComment))),
            cancellationToken);

        // Null when the call failed, or when a 200 carried no verification envelope — the graph
        // broke its promise, or an agent service from before the route. Both are the service
        // not answering the question, so both are retried.
        var verdict = call.Ok ? call.Response?.VerificationResult() : null;

        var (validationResult, error) = verdict switch
        {
            null => ("CallFailed", call.Error ?? "The agent service's reply carried no verification result."),
            { Succeeded: false } => ("SafeFailure", verdict.Error ?? "The verification agent could not give a verdict."),
            _ => ("Ok", (string?)null)
        };

        // The AUDIT COPY, on the report's workflow beside the repair: the agent's output
        // verbatim, never edited. "[]" tool calls, like every agent-level step — the agent's own
        // tool calls are rows of their own, written by InternalToolsController.
        _db.AgentSteps.Add(new AgentStep
        {
            WorkflowId = workflowId.Value,
            AgentName = AgentRunResponse.VerificationAgentName,
            ToolCallsJson = "[]",
            PayloadJson = verdict?.OutputJson,
            DurationMs = verdict?.DurationMs ?? call.DurationMs,
            Attempts = verdict?.Attempts,
            ValidationResult = validationResult,
            ErrorMessage = Truncate(error, MaxStepErrorLength)
        });

        // QUEUED AGAIN WHILE THE AGENT WAS WORKING — the reporter answered a check that was sent
        // as silent. This verdict is about the silence, so it is kept on its step and nowhere
        // else, and the check stays waiting to be judged with the answer. Read fresh from the
        // database: the tracked copy still holds the stamp this run started with.
        var currentStamp = await _db.VerificationChecks
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => v.AgentQueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentStamp != queuedStamp)
        {
            _logger.LogInformation(
                "Verification check {CheckId} was queued again while the agent judged it; its verdict is on the step only.",
                check.Id);
            await _db.SaveChangesAsync(cancellationToken);
            return CheckOutcome.Skipped;
        }

        CheckOutcome outcome;

        if (validationResult == "Ok")
        {
            // The agent's OPINION, beside the reporter's answer. Status is not touched: nothing
            // acts on this label (see VerificationCheck.AgentOutcome).
            check.AgentOutcome = Truncate(verdict!.Outcome, MaxOutcomeLength);
            check.AgentReason = Truncate(verdict.Reason, MaxReasonLength);
            check.AgentEvidenceJson = verdict.EvidenceJson;
            check.AgentError = null;
            check.AgentJudgedAt = UtcNow;
            outcome = CheckOutcome.Judged;
        }
        else if (validationResult == "SafeFailure")
        {
            // Final: the agent retried its model once inside the call already.
            GiveUp(check, error);
            outcome = CheckOutcome.GaveUp;
        }
        else if (check.AgentAttempts >= VerificationAgentRules.MaxAttempts)
        {
            GiveUp(check, $"Gave up after {check.AgentAttempts} attempts. {error}");
            outcome = CheckOutcome.GaveUp;
        }
        else
        {
            // Left waiting; the next pass tries again.
            check.AgentError = Truncate(error, MaxAgentErrorLength);
            outcome = CheckOutcome.Retrying;
        }

        // The step and what it means for the check, in ONE save: a check never says Judged with
        // no step behind it, nor carries a step its columns do not reflect.
        await _db.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    public async Task<bool> IsJudgingOnWorkflowAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        var reportId = await _db.AgentWorkflows
            .Where(w => w.Id == workflowId)
            .Select(w => w.ReportId)
            .FirstOrDefaultAsync(cancellationToken);

        if (reportId is null)
        {
            return false;
        }

        // Only the report's LATEST workflow — the one the runner sends. An older ended run of
        // the same report keeps refusing calls.
        var latestId = await _db.AgentWorkflows
            .Where(w => w.ReportId == reportId)
            .MaxAsync(w => w.Id, cancellationToken);

        if (latestId != workflowId)
        {
            return false;
        }

        return await _db.VerificationChecks
            .Where(v => v.WorkOrder!.ReportId == reportId)
            .Where(VerificationAgentRules.AwaitingJudgement)
            .AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Ended without a verdict: AgentJudgedAt stamped so it is not picked again, AgentOutcome
    /// left null, AgentError saying why. A check queued again later (a late answer) starts over.
    /// </summary>
    private void GiveUp(VerificationCheck check, string? reason)
    {
        check.AgentError = Truncate(reason, MaxAgentErrorLength);
        check.AgentJudgedAt = UtcNow;
    }

    /// <summary>Null or 1–300 characters, the agent's bound: a blank comment goes as none.</summary>
    private static string? CommentForAgent(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var trimmed = comment.Trim();
        return trimmed.Length <= MaxReporterCommentLength ? trimmed : trimmed[..MaxReporterCommentLength];
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
