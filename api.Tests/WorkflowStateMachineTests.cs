using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// An ApiFactory holding the people and the equipment every state-machine case files its
/// report against, built once — the cases differ only in the workflow's state and the move.
/// </summary>
public class StateMachineApiFactory : ApiFactory
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StateMachineScene? _scene;

    public async Task<StateMachineScene> SceneAsync()
    {
        await _gate.WaitAsync();

        try
        {
            return _scene ??= await StateMachineScene.BuildAsync(this);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public record StateMachineScene(
    HttpClient Manager,
    HttpClient Reporter,
    HttpClient Technician,
    int TechnicianId,
    int RoomId,
    int AssetId)
{
    public static async Task<StateMachineScene> BuildAsync(ApiFactory factory)
    {
        var anonymous = factory.CreateClient();
        var code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var building = await (await anonymous.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", code), WorkflowStateMachineTests.JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(WorkflowStateMachineTests.JsonOptions);

        var room = await (await anonymous.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", code, 1), WorkflowStateMachineTests.JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(WorkflowStateMachineTests.JsonOptions);

        var (admin, _) = await ClientAsync(factory, Role.Admin);

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {code}", 24), WorkflowStateMachineTests.JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(WorkflowStateMachineTests.JsonOptions);

        var asset = await (await admin.PostAsJsonAsync(
                "/api/assets",
                new CreateAssetDto(code, "Ceiling Projector", category!.Id, room!.Id, "Acme", "X1",
                    new DateOnly(2024, 1, 15), null),
                WorkflowStateMachineTests.JsonOptions))
            .Content.ReadFromJsonAsync<AssetDto>(WorkflowStateMachineTests.JsonOptions);

        var (manager, _) = await ClientAsync(factory, Role.FacilitiesManager);
        var (reporter, _) = await ClientAsync(factory, Role.Reporter);
        var (technician, technicianId) = await ClientAsync(factory, Role.Technician);

        return new StateMachineScene(manager, reporter, technician, technicianId, room.Id, asset!.Id);
    }

    public async Task<int> FileReportAsync()
    {
        var response = await Reporter.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Projector keeps cutting out mid-lecture.", RoomId),
            WorkflowStateMachineTests.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReportDto>(WorkflowStateMachineTests.JsonOptions))!.Id;
    }

    private static async Task<(HttpClient Client, int UserId)> ClientAsync(ApiFactory factory, Role role)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "MachinePass1", "Test User", role),
            WorkflowStateMachineTests.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(WorkflowStateMachineTests.JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }
}

/// <summary>
/// The workflow state machine (WorkflowTransitions), end to end:
///
///   * the table is exactly DEVELOPMENT_GUIDE.md §8 — every legal (state, trigger) pinned;
///   * every OTHER pair throws and leaves the state alone;
///   * a state assigned without going through the machine is refused on save;
///   * EVERY ILLEGAL TRANSITION THROUGH THE API IS A 409 AND WRITES NOTHING — each endpoint
///     that moves a workflow, from every state its trigger may not happen in.
///
/// The legal paths through the endpoints are WorkOrderEndpointTests', ApprovalTests' and
/// ClarificationTests'; the runner's are WorkflowRunnerTests'; the sweep's are
/// VerificationSweepTests'.
/// </summary>
public class WorkflowStateMachineTests : IClassFixture<StateMachineApiFactory>
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Note = "Replaced the lamp and cleaned the intake filter.";

    private readonly StateMachineApiFactory _factory;

    public WorkflowStateMachineTests(StateMachineApiFactory factory) => _factory = factory;

    // ---------------------------------------------------------------------------------
    // The table
    // ---------------------------------------------------------------------------------

    /// <summary>§8, written out a second time on purpose: a changed table must change this too.</summary>
    private static readonly (WorkflowState From, WorkflowTrigger Trigger, WorkflowState To)[] Section8 =
    {
        (WorkflowState.Submitted, WorkflowTrigger.ClarifierAsked, WorkflowState.AwaitingClarification),
        (WorkflowState.Submitted, WorkflowTrigger.ClarifierFoundNothing, WorkflowState.Diagnosing),
        (WorkflowState.Submitted, WorkflowTrigger.AgentFailed, WorkflowState.Failed),
        (WorkflowState.AwaitingClarification, WorkflowTrigger.ReporterAnswered, WorkflowState.Diagnosing),
        (WorkflowState.Diagnosing, WorkflowTrigger.Diagnosed, WorkflowState.Strategizing),
        (WorkflowState.Diagnosing, WorkflowTrigger.AgentFailed, WorkflowState.Failed),
        (WorkflowState.Strategizing, WorkflowTrigger.WorkOrderAutoApproved, WorkflowState.WorkOrderRaised),
        (WorkflowState.Strategizing, WorkflowTrigger.WorkOrderNeedsApproval, WorkflowState.AwaitingManagerApproval),
        (WorkflowState.Strategizing, WorkflowTrigger.AgentFailed, WorkflowState.Failed),
        (WorkflowState.AwaitingManagerApproval, WorkflowTrigger.ManagerApproved, WorkflowState.WorkOrderRaised),
        (WorkflowState.AwaitingManagerApproval, WorkflowTrigger.ManagerRejected, WorkflowState.Closed),
        (WorkflowState.AwaitingManagerApproval, WorkflowTrigger.RevisionRequested, WorkflowState.Strategizing),
        (WorkflowState.WorkOrderRaised, WorkflowTrigger.WorkStarted, WorkflowState.InProgress),
        (WorkflowState.WorkOrderRaised, WorkflowTrigger.WorkCompleted, WorkflowState.Completed),
        (WorkflowState.InProgress, WorkflowTrigger.WorkCompleted, WorkflowState.Completed),
        (WorkflowState.Completed, WorkflowTrigger.VerificationDue, WorkflowState.AwaitingVerification),
        (WorkflowState.AwaitingVerification, WorkflowTrigger.RepairVerified, WorkflowState.Closed),
        (WorkflowState.AwaitingVerification, WorkflowTrigger.RepairReopened, WorkflowState.Diagnosing),
        (WorkflowState.AwaitingVerification, WorkflowTrigger.RepairEscalated, WorkflowState.AwaitingManagerApproval),
        (WorkflowState.Failed, WorkflowTrigger.WorkOrderAutoApproved, WorkflowState.WorkOrderRaised),
        (WorkflowState.Failed, WorkflowTrigger.WorkOrderNeedsApproval, WorkflowState.AwaitingManagerApproval)
    };

    [Fact]
    public void TheTableIsExactlySection8_AndClosedHasNoWayOut()
    {
        Assert.Equal(
            Section8.OrderBy(t => t.From).ThenBy(t => t.Trigger),
            WorkflowTransitions.All.OrderBy(t => t.From).ThenBy(t => t.Trigger));

        Assert.DoesNotContain(WorkflowTransitions.All, t => t.From == WorkflowState.Closed);
        Assert.DoesNotContain(WorkflowTransitions.All, t => t.From == t.To);
    }

    public static IEnumerable<object[]> IllegalPairs() =>
        from state in Enum.GetValues<WorkflowState>()
        from trigger in Enum.GetValues<WorkflowTrigger>()
        where WorkflowTransitions.Next(state, trigger) is null
        select new object[] { state, trigger };

    [Theory]
    [MemberData(nameof(IllegalPairs))]
    public void EveryIllegalTransition_Throws_AndLeavesTheStateAlone(WorkflowState state, WorkflowTrigger trigger)
    {
        var workflow = new AgentWorkflow { Id = 7, Objective = "A fault.", CurrentState = state };

        var ex = Assert.Throws<InvalidWorkflowTransitionException>(() => WorkflowTransitions.Move(workflow, trigger));

        Assert.Equal(state, workflow.CurrentState);
        Assert.Equal(state, ex.From);
        Assert.Equal(trigger, ex.Trigger);
    }

    [Fact]
    public void EveryLegalTransition_LandsWhereTheTableSays()
    {
        foreach (var (from, trigger, to) in Section8)
        {
            var workflow = new AgentWorkflow { Objective = "A fault.", CurrentState = from };

            WorkflowTransitions.Move(workflow, trigger);

            Assert.Equal(to, workflow.CurrentState);
        }
    }

    /// <summary>
    /// The backstop: somebody assigns CurrentState directly, skipping WorkflowTransitions,
    /// and the save is refused. Verified to fail with the check in AppDbContext removed.
    /// </summary>
    [Fact]
    public async Task AStateAssignedWithoutTheMachine_IsRefusedOnSave()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workflow = new AgentWorkflow { Objective = "Assigned around the machine." };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        workflow.CurrentState = WorkflowState.Closed;

        var ex = await Assert.ThrowsAsync<InvalidWorkflowTransitionException>(() => db.SaveChangesAsync());
        Assert.Equal(WorkflowState.Submitted, ex.From);
        Assert.Equal(WorkflowState.Closed, ex.To);

        db.ChangeTracker.Clear();
        var stored = await db.AgentWorkflows.AsNoTracking().SingleAsync(w => w.Id == workflow.Id);
        Assert.Equal(WorkflowState.Submitted, stored.CurrentState);
    }

    // ---------------------------------------------------------------------------------
    // Through the API
    // ---------------------------------------------------------------------------------

    /// <summary>Each endpoint that moves a workflow, named by what it does.</summary>
    public enum ApiMove
    {
        RaiseWorkOrderUnderThreshold,
        RaiseWorkOrderOverThreshold,
        Approve,
        Reject,
        RequestRevision,
        Complete,
        AnswerClarification
    }

    private static WorkflowTrigger TriggerOf(ApiMove move) => move switch
    {
        ApiMove.RaiseWorkOrderUnderThreshold => WorkflowTrigger.WorkOrderAutoApproved,
        ApiMove.RaiseWorkOrderOverThreshold => WorkflowTrigger.WorkOrderNeedsApproval,
        ApiMove.Approve => WorkflowTrigger.ManagerApproved,
        ApiMove.Reject => WorkflowTrigger.ManagerRejected,
        ApiMove.RequestRevision => WorkflowTrigger.RevisionRequested,
        ApiMove.Complete => WorkflowTrigger.WorkCompleted,
        ApiMove.AnswerClarification => WorkflowTrigger.ReporterAnswered,
        _ => throw new ArgumentOutOfRangeException(nameof(move))
    };

    /// <summary>Every endpoint, from every state its trigger may not happen in.</summary>
    public static IEnumerable<object[]> IllegalMovesThroughTheApi() =>
        from move in Enum.GetValues<ApiMove>()
        from state in Enum.GetValues<WorkflowState>()
        where WorkflowTransitions.Next(state, TriggerOf(move)) is null
        select new object[] { move, state };

    [Theory]
    [MemberData(nameof(IllegalMovesThroughTheApi))]
    public async Task EveryIllegalTransitionThroughTheApi_Is409_AndWritesNothing(ApiMove move, WorkflowState state)
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();

        HttpResponseMessage response;
        int? orderId = null;
        int? questionId = null;

        switch (move)
        {
            case ApiMove.RaiseWorkOrderUnderThreshold:
            case ApiMove.RaiseWorkOrderOverThreshold:
                await WorkflowTestData.PutInStateAsync(_factory.Services, reportId, state);
                response = await PostRaiseAsync(scene, reportId,
                    move == ApiMove.RaiseWorkOrderUnderThreshold ? 500m : 42_000m);
                break;

            case ApiMove.Approve:
            case ApiMove.Reject:
            case ApiMove.RequestRevision:
                orderId = await RaiseAsync(scene, reportId, 42_000m, WorkOrderStatus.AwaitingApproval);
                await WorkflowTestData.PutInStateAsync(_factory.Services, reportId, state);
                response = move switch
                {
                    ApiMove.Approve => await scene.Manager.PostAsync($"/api/workorders/{orderId}/approve", null),
                    ApiMove.Reject => await scene.Manager.PostAsJsonAsync(
                        $"/api/workorders/{orderId}/reject", new RejectWorkOrderDto("Not this term."), JsonOptions),
                    _ => await scene.Manager.PostAsJsonAsync(
                        $"/api/workorders/{orderId}/request-revision", new RequestRevisionDto("Try a repair first."), JsonOptions)
                };
                break;

            case ApiMove.Complete:
                orderId = await RaiseAsync(scene, reportId, 500m, WorkOrderStatus.Approved);
                var assigned = await scene.Manager.PutAsJsonAsync(
                    $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(scene.TechnicianId), JsonOptions);
                Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
                await WorkflowTestData.PutInStateAsync(_factory.Services, reportId, state);
                response = await scene.Technician.PostAsJsonAsync($"/api/workorders/{orderId}/complete",
                    new CompleteWorkOrderDto(480m, ServiceOutcome.Resolved, Note, null), JsonOptions);
                break;

            case ApiMove.AnswerClarification:
                questionId = await AskOneQuestionAsync(scene, reportId);
                await WorkflowTestData.PutInStateAsync(_factory.Services, reportId, state);
                response = await scene.Reporter.PostAsJsonAsync($"/api/reports/{reportId}/clarifications",
                    new SubmitAnswersRequest(new[] { new SubmittedAnswer(questionId.Value, "Yes") }), JsonOptions);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(move));
        }

        // A 409 from the MACHINE — not some other conflict an endpoint has of its own.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.Equal("Illegal workflow transition", problem!.Title);

        Assert.Equal(state, await WorkflowTestData.StateAsync(_factory.Services, reportId));

        // ...and nothing the move would have written alongside it.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        switch (move)
        {
            case ApiMove.RaiseWorkOrderUnderThreshold:
            case ApiMove.RaiseWorkOrderOverThreshold:
                Assert.False(await db.WorkOrders.AnyAsync(w => w.ReportId == reportId));
                break;

            case ApiMove.Approve:
            case ApiMove.Reject:
            case ApiMove.RequestRevision:
                var waiting = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == orderId);
                Assert.Equal(WorkOrderStatus.AwaitingApproval, waiting.Status);
                Assert.Null(waiting.ApprovedByUserId);
                Assert.Null(waiting.RejectionReason);
                Assert.Null(waiting.RevisionNote);
                break;

            case ApiMove.Complete:
                var live = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == orderId);
                Assert.Equal(WorkOrderStatus.Approved, live.Status);
                Assert.Null(live.CompletedAt);
                Assert.False(await db.ServiceRecords.AnyAsync(s => s.WorkOrderId == orderId));
                Assert.False(await db.VerificationChecks.AnyAsync(v => v.WorkOrderId == orderId));
                break;

            case ApiMove.AnswerClarification:
                Assert.False(await db.ClarificationAnswers.AnyAsync(a => a.ClarificationQuestionId == questionId));
                var report = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
                Assert.Equal(ReportStatus.AwaitingClarification, report.Status);
                break;
        }
    }

    /// <summary>
    /// The hole a state-only table would leave: an order is waiting on a manager, and a
    /// second order raised on the same report must not take AwaitingManagerApproval ->
    /// WorkOrderRaised — that edge is an APPROVAL. A 409, and the first order still waits.
    /// </summary>
    [Fact]
    public async Task ASecondOrderOnTheSameReport_CannotPassForAnApproval()
    {
        var scene = await _factory.SceneAsync();
        var reportId = await scene.FileReportAsync();

        var waiting = await RaiseAsync(scene, reportId, 42_000m, WorkOrderStatus.AwaitingApproval);

        var second = await PostRaiseAsync(scene, reportId, 500m);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, await WorkflowTestData.StateAsync(_factory.Services, reportId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(waiting, (await db.WorkOrders.SingleAsync(w => w.ReportId == reportId)).Id);
    }

    // ---------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostRaiseAsync(StateMachineScene scene, int reportId, decimal cost) =>
        scene.Manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(reportId, scene.AssetId, WorkOrderStrategy.SingleJob, cost, null),
            JsonOptions);

    /// <summary>Raised the legal way — from Strategizing — so the case can then move the workflow somewhere else.</summary>
    private async Task<int> RaiseAsync(StateMachineScene scene, int reportId, decimal cost, WorkOrderStatus expected)
    {
        await WorkflowTestData.ReadyForWorkOrderAsync(_factory.Services, reportId);

        var response = await PostRaiseAsync(scene, reportId, cost);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);
        Assert.Equal(expected, order!.Status);
        return order.Id;
    }

    /// <summary>One yes/no question on the report — which moves the REPORT to AwaitingClarification.</summary>
    private async Task<int> AskOneQuestionAsync(StateMachineScene scene, int reportId)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workflowId = await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.Id).SingleAsync();

            await scope.ServiceProvider.GetRequiredService<IClarificationService>().RecordQuestionsAsync(
                reportId, workflowId,
                new[] { new ParsedClarifyingQuestion("Is the power light on?", AnswerType.YesNo, null, 0) });
        }

        var questions = await scene.Reporter.GetFromJsonAsync<List<ClarificationQuestionDto>>(
            $"/api/reports/{reportId}/clarifications", JsonOptions);

        return Assert.Single(questions!).Id;
    }
}
