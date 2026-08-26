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

    public WorkflowService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<WorkflowSummaryDto> StartAsync(
        StartWorkflowRequest dto,
        CancellationToken cancellationToken = default)
    {
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
        var steps = workflow.Steps
            .OrderBy(s => s.Id)
            .Select(ToStepDto)
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
            workflow.CreatedAt,
            workflow.UpdatedAt,
            steps);
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

    public async Task<bool> BeginProcessingAsync(int workflowId, CancellationToken cancellationToken = default)
    {
        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null)
        {
            return false;
        }

        // Only a freshly submitted workflow may be started. Anything else is either
        // already running or finished, and a second queue entry must not rewind it.
        if (workflow.CurrentState != WorkflowState.Submitted)
        {
            return false;
        }

        workflow.StartedAt = DateTime.UtcNow;
        workflow.CurrentState = WorkflowState.Diagnosing;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> FailAsync(int workflowId, string reason, CancellationToken cancellationToken = default)
    {
        var workflow = await _db.AgentWorkflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null)
        {
            return false;
        }

        workflow.CurrentState = WorkflowState.Failed;
        workflow.Outcome = reason;
        workflow.CompletedAt = DateTime.UtcNow;

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
