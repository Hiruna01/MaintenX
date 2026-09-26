using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class WorkflowService : IWorkflowService
{
    /// <summary>Largest page a client may ask for, so one request cannot pull the table.</summary>
    private const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    private readonly IWorkflowQueue _workflowQueue;

    public WorkflowService(AppDbContext db, IWorkflowQueue workflowQueue)
    {
        _db = db;
        _workflowQueue = workflowQueue;
    }

    public async Task<WorkflowSummaryDto?> StartAsync(
        StartWorkflowRequest dto,
        CancellationToken cancellationToken = default)
    {
        // ReportId is a foreign key, so a bad one has to be caught here to be a 400
        // rather than a 500. Null is legitimate — a workflow may be started from a bare
        // objective — so only a supplied value is checked.
        if (dto.ReportId is not null
            && !await _db.Reports.AnyAsync(r => r.Id == dto.ReportId, cancellationToken))
        {
            return null;
        }

        var workflow = new AgentWorkflow
        {
            ReportId = dto.ReportId,
            Objective = dto.Objective,
            // Every workflow starts here. Moving it on is the background runner's job.
            CurrentState = WorkflowState.Submitted
        };

        _db.AgentWorkflows.Add(workflow);
        await _db.SaveChangesAsync(cancellationToken);

        return ToSummaryDto(workflow);
    }

    public async Task<WorkflowDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var workflow = await _db.AgentWorkflows
            .AsNoTracking()
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (workflow is null)
        {
            return null;
        }

        // Steps come back in the order they happened. Id is monotonic per insert, so it
        // orders identically to CreatedAt but without ties inside the same millisecond.
        var ordered = workflow.Steps.OrderBy(s => s.Id).ToList();
        var steps = ordered.Select(ToStepDto).ToList();

        // The diagnostic's own answers, not its tool calls — those carry the same AgentName.
        // Filtered in memory, because the difference is inside a jsonb column.
        var diagnoses = ordered
            .Where(s => s.AgentName == AgentRunResponse.DiagnosticAgentName && AgentAnalysis.IsAgentRunStep(s))
            .Select(AgentAnalysis.ToDiagnosis)
            .ToList();

        return new WorkflowDetailDto(
            workflow.Id,
            workflow.ReportId,
            workflow.Objective,
            workflow.CurrentState,
            workflow.PlanJson,
            workflow.Outcome,
            workflow.StartedAt,
            workflow.CompletedAt,
            workflow.ReopenedWorkOrderId,
            workflow.CreatedAt,
            workflow.UpdatedAt,
            steps,
            diagnoses);
    }

    public async Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(
        WorkflowState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject: a client asking for page 0 gets page 1, not a 400.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.AgentWorkflows.AsNoTracking();

        if (state is not null)
        {
            query = query.Where(w => w.CurrentState == state);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => ToSummaryDto(w))
            .ToListAsync(cancellationToken);

        return new PagedResult<WorkflowSummaryDto>(items, page, pageSize, totalCount);
    }

    public Task<bool> ExistsAsync(int workflowId, CancellationToken cancellationToken = default) =>
        _db.AgentWorkflows.AnyAsync(w => w.Id == workflowId, cancellationToken);

    public async Task<bool> RecordStepAsync(
        int workflowId,
        string agentName,
        string? toolCallsJson,
        string? payloadJson,
        int durationMs,
        string? validationResult,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(workflowId, cancellationToken))
        {
            return false;
        }

        _db.AgentSteps.Add(new AgentStep
        {
            WorkflowId = workflowId,
            AgentName = agentName,
            ToolCallsJson = toolCallsJson,
            PayloadJson = payloadJson,
            DurationMs = durationMs,
            ValidationResult = validationResult,
            ErrorMessage = errorMessage
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WorkflowState?> BeginProcessingAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        // The only two states an agent run starts from. Anything else is waiting on a
        // person or already past the agents, and a stray queue entry must not rewind it.
        if (workflow is null
            || workflow.CurrentState is not (WorkflowState.Submitted or WorkflowState.Diagnosing))
        {
            return null;
        }

        // When the runner FIRST picked it up — a resume after clarification keeps it.
        workflow.StartedAt ??= DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return workflow.CurrentState;
    }

    public Task<bool> FailAsync(int workflowId, string reason, CancellationToken cancellationToken = default) =>
        MoveAsync(workflowId, WorkflowTrigger.AgentFailed, reason, completed: true, cancellationToken);

    public Task<bool> CompleteClarificationAsync(
        int workflowId,
        int questionCount,
        CancellationToken cancellationToken = default) =>
        MoveAsync(
            workflowId,
            WorkflowTransitions.ForClarification(questionCount),
            questionCount > 0
                // The questions themselves are on the AgentStep and in ClarificationQuestion
                // rows; POST /api/reports/{id}/clarifications is what moves it on again.
                ? $"The clarifier asked {questionCount} question(s) about this report."
                : "The clarifier found nothing that needed clarifying.",
            completed: false,
            cancellationToken);

    public Task<bool> CompleteDiagnosisAsync(
        int workflowId,
        bool diagnosed,
        CancellationToken cancellationToken = default) =>
        MoveAsync(
            workflowId,
            WorkflowTrigger.Diagnosed,
            diagnosed
                ? "Diagnosed; the strategist is proposing a resolution."
                : "The diagnostic produced no diagnosis; the strategist is proposing without one.",
            completed: false,
            cancellationToken);

    public async Task<bool> RecordProposalAsync(
        int workflowId,
        bool proposed,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null)
        {
            return false;
        }

        workflow.Outcome = proposed
            ? "The strategist has proposed a resolution. Waiting for a facilities manager to raise the work order."
            : "The strategist produced no proposal. Waiting for a facilities manager to raise the work order.";

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReopenForDiagnosisAsync(int workOrderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.WorkOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);

        // A repair that never finished cannot have failed to hold.
        if (order is null || order.Status != WorkOrderStatus.Completed)
        {
            return false;
        }

        // The latest run raised for the report, the same one WorkOrderService moved when the
        // order was raised and completed.
        var workflow = await _db.AgentWorkflows
            .Where(w => w.ReportId == order.ReportId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (workflow is null)
        {
            return false;
        }

        // Throws before anything is written unless the workflow is AwaitingVerification.
        WorkflowTransitions.Move(workflow, WorkflowTrigger.RepairReopened);
        workflow.ReopenedWorkOrderId = order.Id;
        workflow.Outcome =
            $"The repair on work order {order.Id} did not hold. The diagnostic is running again "
            + "against the service history and reports since.";

        await _db.SaveChangesAsync(cancellationToken);

        // AFTER the save, so the runner never dequeues an id whose state is not written yet;
        // CancellationToken.None so a caller's cancelled token cannot abort the hand-off.
        // Same reasoning as ClarificationService.
        await _workflowQueue.EnqueueAsync(workflow.Id, CancellationToken.None);

        return true;
    }

    /// <summary>
    /// One transition, saved on its own. Through WorkflowTransitions.Move, so an illegal one
    /// throws before anything is written. <paramref name="completed"/> stamps CompletedAt
    /// for a run that has ended.
    /// </summary>
    private async Task<bool> MoveAsync(
        int workflowId,
        WorkflowTrigger trigger,
        string outcome,
        bool completed,
        CancellationToken cancellationToken)
    {
        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null)
        {
            return false;
        }

        WorkflowTransitions.Move(workflow, trigger);
        workflow.Outcome = outcome;

        if (completed)
        {
            workflow.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static WorkflowSummaryDto ToSummaryDto(AgentWorkflow w) =>
        new(w.Id, w.ReportId, w.Objective, w.CurrentState, w.Outcome,
            w.StartedAt, w.CompletedAt, w.CreatedAt, w.UpdatedAt);

    private static AgentStepDto ToStepDto(AgentStep s) =>
        new(s.Id, s.WorkflowId, s.AgentName, s.ToolCallsJson, s.DurationMs,
            s.ValidationResult, s.ErrorMessage, s.PayloadJson, s.CreatedAt, s.UpdatedAt);
}
