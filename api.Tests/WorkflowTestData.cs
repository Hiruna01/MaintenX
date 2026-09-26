using CampusFacilities.Api.Data;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace api.Tests;

/// <summary>
/// Puts a report's workflow where a test needs it to start.
///
/// The runner is removed from the container in every test (see ApiFactory), so a report
/// filed through POST /api/reports leaves its workflow in Submitted — and a work order may
/// only be raised from Strategizing. These write the state AS DATA, with ExecuteUpdate,
/// deliberately going around WorkflowTransitions and AppDbContext's save-time check: this is
/// test setup standing in for a run that happened, the way DbSeeder writes its rows, not a
/// transition anything in the application makes.
/// </summary>
internal static class WorkflowTestData
{
    /// <summary>Where a completed agent run leaves a report: ready for a manager to raise its order.</summary>
    public static Task ReadyForWorkOrderAsync(IServiceProvider services, int reportId) =>
        PutInStateAsync(services, reportId, WorkflowState.Strategizing);

    /// <summary>The latest workflow raised for the report, written straight into <paramref name="state"/>.</summary>
    public static async Task PutInStateAsync(IServiceProvider services, int reportId, WorkflowState state)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workflowId = await db.AgentWorkflows
            .Where(w => w.ReportId == reportId)
            .MaxAsync(w => w.Id);

        await db.AgentWorkflows
            .Where(w => w.Id == workflowId)
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.CurrentState, state));
    }

    /// <summary>
    /// Where a reporter's "no" leaves the report's latest workflow: Diagnosing, remembering
    /// the order that did not hold. Test setup standing in for the confirm, like the rest.
    /// </summary>
    public static async Task ReopenedAsync(IServiceProvider services, int reportId, int workOrderId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workflowId = await db.AgentWorkflows
            .Where(w => w.ReportId == reportId)
            .MaxAsync(w => w.Id);

        await db.AgentWorkflows
            .Where(w => w.Id == workflowId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(w => w.CurrentState, WorkflowState.Diagnosing)
                .SetProperty(w => w.ReopenedWorkOrderId, workOrderId));
    }

    public static async Task<WorkflowState> StateAsync(IServiceProvider services, int reportId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows
            .Where(w => w.ReportId == reportId)
            .OrderByDescending(w => w.Id)
            .Select(w => w.CurrentState)
            .FirstAsync();
    }
}
