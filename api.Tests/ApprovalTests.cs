using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace api.Tests;

/// <summary>
/// The approval gate and the manager's decisions — the cases WorkOrderEndpointTests does not
/// already pin. That class has under / over / a cent over the threshold, EscalateReplacement
/// at Rs 100, a Technician's 403, the 409 for a second approval, reject without a reason and
/// request-revision back to Strategizing. What is added here:
///
///   * EXACTLY ON THE THRESHOLD, FOR EVERY STRATEGY. The rule is "strictly ABOVE": an
///     estimate equal to Approval:CostThreshold does NOT need a manager — unless it is a
///     replacement, which always does. Both halves at the one value where they diverge.
///   * EscalateReplacement at every cost that matters, Rs 0 included.
///   * No token is 401 on every decision, not only on create.
///   * A decision, once made, is not rewritten by the opposite one — sequentially, and when
///     another decision lands between a manager's read and their write (the claim).
///   * Every approval event is a step on the workflow's audit trail (ApprovalAudit).
/// </summary>
public class ApprovalTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApprovalTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const decimal Threshold = ApprovalSettings.DefaultCostThreshold;

    // ---------------------------------------------------------------------------------
    // The boundary
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// COST == THRESHOLD DOES NOT REQUIRE APPROVAL. The threshold is the most that may be
    /// spent without a manager, so an estimate sitting exactly on it is inside the limit and
    /// is approved on the spot. Only EscalateReplacement goes to a manager at this cost — for
    /// being a replacement, not for the money. The approval basis says the same thing the
    /// status does: not above the threshold.
    /// </summary>
    [Theory]
    [InlineData(WorkOrderStrategy.KnownFix, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.SingleJob, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.ConsolidatedJob, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.InspectFirst, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.Defer, WorkOrderStatus.Approved)]
    [InlineData(WorkOrderStrategy.EscalateReplacement, WorkOrderStatus.AwaitingApproval)]
    public async Task AtExactlyTheThreshold_OnlyAReplacementNeedsAManager(
        WorkOrderStrategy strategy, WorkOrderStatus expected)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);

        var order = await RaiseAsync(manager, await NewFaultAsync(), Threshold, strategy);

        Assert.Equal(expected, order.Status);

        var basis = (await DetailAsync(manager, order.Id)).ApprovalBasis;
        Assert.Equal(Threshold, basis.Threshold);
        Assert.False(basis.ExceedsThreshold);
        Assert.Equal(strategy == WorkOrderStrategy.EscalateReplacement, basis.RequiresApproval);
    }

    /// <summary>
    /// A replacement needs a manager whatever it costs — free, a cent under the threshold, on
    /// it, or over it. Replacing equipment is a decision about the estate, not only about money.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("14999.99")]
    [InlineData("15000")]
    [InlineData("15000.01")]
    [InlineData("250000")]
    public async Task EscalateReplacement_NeedsAManager_AtAnyCost(string cost)
    {
        // Strings, because an attribute cannot hold a decimal — and never via a double.
        var estimate = decimal.Parse(cost, CultureInfo.InvariantCulture);
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        var order = await RaiseAsync(manager, fault, estimate, WorkOrderStrategy.EscalateReplacement);

        Assert.Equal(WorkOrderStatus.AwaitingApproval, order.Status);
        Assert.True((await DetailAsync(manager, order.Id)).ApprovalBasis.IsReplacement);
        Assert.Equal(WorkflowState.AwaitingManagerApproval, await WorkflowStateAsync(fault.ReportId));
    }

    /// <summary>
    /// A RESUBMITTED DRAFT GOES THROUGH THE SAME GATE: sent back for revision, then resubmitted
    /// at exactly the threshold, it lands where a new order at that cost would — approved for
    /// every strategy but a replacement — and the same order moves on, not a second one. The
    /// gate leaves its step after the revision's, saying who resubmitted it. Verified to fail
    /// with ResubmitAsync setting the status itself instead of calling the gate.
    /// </summary>
    [Theory]
    [InlineData(WorkOrderStrategy.KnownFix, WorkOrderStatus.Approved, WorkflowState.WorkOrderRaised)]
    [InlineData(WorkOrderStrategy.Defer, WorkOrderStatus.Approved, WorkflowState.WorkOrderRaised)]
    [InlineData(WorkOrderStrategy.EscalateReplacement, WorkOrderStatus.AwaitingApproval, WorkflowState.AwaitingManagerApproval)]
    public async Task AResubmittedDraft_AtExactlyTheThreshold_IsRoutedByTheSameGate(
        WorkOrderStrategy strategy, WorkOrderStatus expected, WorkflowState expectedWorkflow)
    {
        var (manager, managerId) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync(
            $"/api/workorders/{order.Id}/request-revision", new RequestRevisionDto("Too dear."), JsonOptions)).StatusCode);
        Assert.Equal(WorkOrderStatus.Draft, (await DetailAsync(manager, order.Id)).Status);

        var resubmitted = await manager.PostAsJsonAsync(
            $"/api/workorders/{order.Id}/resubmit", new ResubmitWorkOrderDto(strategy, Threshold, "Fan and filter."), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, resubmitted.StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(expected, detail.Status);
        Assert.Equal(strategy, detail.Strategy);
        Assert.Equal(Threshold, detail.EstimatedCost);
        Assert.Equal(strategy == WorkOrderStrategy.EscalateReplacement, detail.ApprovalBasis.RequiresApproval);
        Assert.Equal(expectedWorkflow, await WorkflowStateAsync(fault.ReportId));

        var steps = await ApprovalStepsAsync(fault.ReportId);
        Assert.Equal(
            new[]
            {
                ApprovalAudit.ApprovalRequired, ApprovalAudit.RevisionRequested,
                expected == WorkOrderStatus.Approved ? ApprovalAudit.AutoApproved : ApprovalAudit.ApprovalRequired
            },
            steps.Select(st => st.Decision));
        Assert.All(steps, st => Assert.Equal(order.Id, st.WorkOrderId));
        Assert.Null(steps[2].DecidedByUserId);
        Assert.Contains($"user {managerId}", steps[2].Note);

        // Still the one order on the report.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(order.Id, (await db.WorkOrders.SingleAsync(w => w.ReportId == fault.ReportId)).Id);
    }

    // ---------------------------------------------------------------------------------
    // Who may decide
    // ---------------------------------------------------------------------------------

    /// <summary>No token is 401 — "who are you?" — on every decision, and nothing moves.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-revision")]
    [InlineData("resubmit")]
    public async Task ManagerDecisions_WithNoToken_Are401(string action)
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/api/workorders/{order.Id}/{action}",
            new { reason = "No reason.", note = "No note." }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(WorkOrderStatus.AwaitingApproval, (await DetailAsync(manager, order.Id)).Status);
    }

    // ---------------------------------------------------------------------------------
    // A decision stands
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ARejectedOrder_CannotThenBeApprovedOrSentForRevision()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        var reject = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Out of budget this term."), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        var approve = await manager.PostAsync($"/api/workorders/{order.Id}/approve", null);
        var revise = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/request-revision",
            new RequestRevisionDto("Try a cheaper fix."), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, revise.StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(WorkOrderStatus.Rejected, detail.Status);
        Assert.Equal("Out of budget this term.", detail.RejectionReason);
    }

    [Fact]
    public async Task AnApprovedOrder_CannotThenBeRejected()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var order = await RaiseAsync(manager, await NewFaultAsync(), 42_000m);

        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PostAsync($"/api/workorders/{order.Id}/approve", null)).StatusCode);

        var reject = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Changed my mind."), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);

        var detail = await DetailAsync(manager, order.Id);
        Assert.Equal(WorkOrderStatus.Approved, detail.Status);
        Assert.Null(detail.RejectionReason);
    }

    /// <summary>
    /// TWO MANAGERS AT ONCE. Another decision lands after this one has read the order as
    /// AwaitingApproval and before it writes — the window the status check at the top of each
    /// decision cannot close on its own. The claim (a conditional UPDATE, first in the
    /// decision's transaction) finds the order already decided and the decision is refused:
    /// no audit step, no workflow move, nothing written.
    ///
    /// The competing decision is played by an interceptor that runs just before the claim's
    /// UPDATE, on the same connection. It shares the loser's transaction, so the rollback
    /// takes it back out again — what is asserted is what the LOSER wrote, which is nothing.
    /// Without the claim this approve returns Success, writes ManagerApproved over the other
    /// decision, and moves the workflow.
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-revision")]
    public async Task ADecisionThatLosesTheRace_WritesNothing(string action)
    {
        var (manager, managerId) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        WorkOrderActionOutcome outcome;

        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await using var db = ContextWith(
                sp.GetRequiredService<AppDbContext>(), new AnotherManagerDecidesFirst(order.Id));

            var service = new WorkOrderService(
                db,
                sp.GetRequiredService<ApprovalSettings>(),
                sp.GetRequiredService<IWorkflowQueue>(),
                sp.GetRequiredService<IVerificationService>(),
                TimeProvider.System,
                sp.GetRequiredService<SchedulingSettings>(),
                sp.GetRequiredService<IAssetService>(),
                sp.GetRequiredService<IFileStorageService>(),
                sp.GetRequiredService<SlaSettings>(),
                NullLogger<WorkOrderService>.Instance);

            outcome = action switch
            {
                "approve" => await service.ApproveAsync(order.Id, managerId),
                "reject" => await service.RejectAsync(order.Id, managerId, "Out of budget this term."),
                _ => await service.RequestRevisionAsync(order.Id, managerId, "Try a cheaper fix first.")
            };
        }

        Assert.Equal(WorkOrderActionOutcome.InvalidState, outcome);

        Assert.Equal(
            new[] { ApprovalAudit.ApprovalRequired },
            (await ApprovalStepsAsync(fault.ReportId)).Select(s => s.Decision));
        Assert.Equal(WorkflowState.AwaitingManagerApproval, await WorkflowStateAsync(fault.ReportId));

        using var verify = _factory.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<AppDbContext>()
            .WorkOrders.AsNoTracking().SingleAsync(w => w.Id == order.Id);
        Assert.Null(stored.ApprovedByUserId);
        Assert.Null(stored.RevisionNote);
        Assert.Null(stored.DueAt);
    }

    // ---------------------------------------------------------------------------------
    // The audit trail — ApprovalAudit
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The human pause is on the WORKFLOW's audit trail, not only on the order: where the gate
    /// routed it (and against which threshold), then who decided, which way, and why. The
    /// order's own columns are overwritten — a revised order is back in Draft — so these
    /// steps are the lasting record.
    /// </summary>
    [Theory]
    [InlineData("approve", ApprovalAudit.ManagerApproved)]
    [InlineData("reject", ApprovalAudit.ManagerRejected)]
    [InlineData("request-revision", ApprovalAudit.RevisionRequested)]
    public async Task EveryApprovalEvent_IsOnTheWorkflowsAuditTrail_WithWhoDecided(string action, string decision)
    {
        var (manager, managerId) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        var response = await manager.PostAsJsonAsync(
            $"/api/workorders/{order.Id}/{action}",
            new { reason = "Out of budget this term.", note = "Try a cheaper fix first." }, JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var steps = await ApprovalStepsAsync(fault.ReportId);
        Assert.Equal(new[] { ApprovalAudit.ApprovalRequired, decision }, steps.Select(s => s.Decision));
        Assert.All(steps, s => Assert.Equal(order.Id, s.WorkOrderId));

        var gate = steps[0];
        Assert.Null(gate.DecidedByUserId);
        Assert.Equal(42_000m, gate.EstimatedCost);
        Assert.Equal(Threshold, gate.Basis.Threshold);
        Assert.True(gate.Basis.ExceedsThreshold);

        var decided = steps[1];
        Assert.Equal(managerId, decided.DecidedByUserId);
        Assert.Equal(action == "reject" ? "Out of budget this term." : null, decided.Reason);
        Assert.Equal(action == "request-revision" ? "Try a cheaper fix first." : null, decided.Note);
    }

    /// <summary>Under the threshold the gate still leaves its step — and says nobody decided.</summary>
    [Fact]
    public async Task AnOrderInsideTheThreshold_IsRecordedAsAutoApproved_WithNobodyDeciding()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();

        await RaiseAsync(manager, fault, Threshold);

        var step = Assert.Single(await ApprovalStepsAsync(fault.ReportId));
        Assert.Equal(ApprovalAudit.AutoApproved, step.Decision);
        Assert.Null(step.DecidedByUserId);
        Assert.False(step.Basis.RequiresApproval);
    }

    /// <summary>A refused decision (409) writes nothing — the step shares the move's save.</summary>
    [Fact]
    public async Task ARefusedDecision_AddsNoStep()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var fault = await NewFaultAsync();
        var order = await RaiseAsync(manager, fault, 42_000m);

        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.PostAsync($"/api/workorders/{order.Id}/approve", null)).StatusCode);
        var reject = await manager.PostAsJsonAsync($"/api/workorders/{order.Id}/reject",
            new RejectWorkOrderDto("Changed my mind."), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);

        Assert.Equal(
            new[] { ApprovalAudit.ApprovalRequired, ApprovalAudit.ManagerApproved },
            (await ApprovalStepsAsync(fault.ReportId)).Select(s => s.Decision));
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    /// <summary>The report's approval steps, oldest first, read back as the payload they store.</summary>
    private async Task<List<ApprovalStepPayload>> ApprovalStepsAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var steps = await db.AgentSteps
            .Where(s => s.Workflow!.ReportId == reportId && s.AgentName == ApprovalAudit.StepName)
            .OrderBy(s => s.Id)
            .ToListAsync();

        Assert.All(steps, s =>
        {
            Assert.Null(s.ToolCallsJson);
            Assert.Null(s.Attempts);
        });

        return steps
            .Select(s => JsonSerializer.Deserialize<ApprovalStepPayload>(s.PayloadJson!, JsonOptions)!)
            .Zip(steps, (payload, step) =>
            {
                // The tag on the row and the payload's own copy must agree.
                Assert.Equal(step.ValidationResult, payload.Decision);
                return payload;
            })
            .ToList();
    }

    /// <summary>
    /// A second context on the same database as <paramref name="db"/>, with an interceptor.
    /// SQLite shares the held-open connection; PostgreSQL reconnects by connection string.
    /// </summary>
    private static AppDbContext ContextWith(AppDbContext db, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();

        if (db.Database.IsNpgsql())
        {
            options.UseNpgsql(db.Database.GetConnectionString());
        }
        else
        {
            options.UseSqlite(db.Database.GetDbConnection());
        }

        return new AppDbContext(options.AddInterceptors(interceptor).Options);
    }

    /// <summary>
    /// Plays the other manager: the first time this context sends an UPDATE to WorkOrders —
    /// the decision's claim — it first marks the order Rejected, on the same connection and
    /// transaction, as a decision that committed a moment earlier would have left it.
    /// </summary>
    private sealed class AnotherManagerDecidesFirst(int orderId) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"WorkOrders\"", StringComparison.Ordinal))
            {
                _done = true;

                await using var competitor = command.Connection!.CreateCommand();
                competitor.Transaction = command.Transaction;
                competitor.CommandText =
                    $"UPDATE \"WorkOrders\" SET \"Status\" = 'Rejected' WHERE \"Id\" = {orderId}";
                await competitor.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private record Fault(int ReportId, int AssetId);

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await _factory.RegisterAsync(new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "ApprovalPass1", "Test User", role));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>A room, an asset in it, and a report filed through POST /api/reports.</summary>
    private async Task<Fault> NewFaultAsync()
    {
        var estate = await _factory.CreateAdminClientAsync();

        var building = await (await estate.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await estate.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var (admin, _) = await ClientAsync(Role.Admin);

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var asset = await (await admin.PostAsJsonAsync(
                "/api/assets",
                new CreateAssetDto(UniqueCode(), "Ceiling Projector", category!.Id, room!.Id, "Acme", "X1",
                    new DateOnly(2024, 1, 15), null),
                JsonOptions))
            .Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        var (reporter, _) = await ClientAsync(Role.Reporter);
        var report = await (await reporter.PostAsJsonAsync(
                "/api/reports", new CreateReportDto("Projector keeps cutting out mid-lecture.", room.Id), JsonOptions))
            .Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        // Where a finished agent run leaves it: a work order is raised from Strategizing.
        await WorkflowTestData.ReadyForWorkOrderAsync(_factory.Services, report!.Id);

        return new Fault(report.Id, asset!.Id);
    }

    private static async Task<WorkOrderDto> RaiseAsync(
        HttpClient manager,
        Fault fault,
        decimal estimatedCost,
        WorkOrderStrategy strategy = WorkOrderStrategy.SingleJob)
    {
        var response = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(fault.ReportId, fault.AssetId, strategy, estimatedCost, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions))!;
    }

    private static async Task<WorkOrderDetailDto> DetailAsync(HttpClient manager, int orderId) =>
        (await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions))!;

    private async Task<WorkflowState> WorkflowStateAsync(int reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.CurrentState).SingleAsync();
    }
}
