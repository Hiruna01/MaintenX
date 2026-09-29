using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class ReportTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReportTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // The API serialises enums by name, so the tests must too — otherwise these tests
    // would pass against a contract the real clients cannot actually use.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>Returns a client carrying a bearer token, and the id of the user it belongs to.</summary>
    private async Task<(HttpClient Client, int UserId)> CreateAuthenticatedClientAsync(
        Role role = Role.Reporter)
    {
        var client = _factory.CreateClient();

        var response = await _factory.RegisterAsync(new RegisterRequest(UniqueEmail(), "ReportPass1", "Test Reporter", role));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>Creates a building and a room in it, returning the room's id.</summary>
    private async Task<int> CreateRoomAsync()
    {
        var client = await _factory.CreateAdminClientAsync();

        var buildingResponse = await client.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, buildingResponse.StatusCode);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await client.PostAsJsonAsync(
            "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        return room!.Id;
    }

    [Fact]
    public async Task CreateReport_Returns201AndIsReadableBack()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The projector keeps cutting out during lectures.", roomId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal(userId, created.ReporterId);
        Assert.Equal(roomId, created.RoomId);
        Assert.Equal(ReportStatus.Submitted, created.Status);

        // CreatedAt/UpdatedAt are stamped by AppDbContext.ApplyTimestamps, whose entity
        // list is hand-maintained — a Report left off it would silently arrive as default.
        Assert.NotEqual(default, created.CreatedAt);
        Assert.NotEqual(default, created.UpdatedAt);

        // CreatedAtAction must point at a GET that actually serves the resource.
        var location = response.Headers.Location;
        Assert.NotNull(location);

        var fetched = await client.GetFromJsonAsync<ReportDetailDto>(location, JsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.Description, fetched.Description);

        // The detail read resolves the room rather than handing back an id, and carries the
        // two audit collections — both empty on a report nothing has happened to yet, which
        // is not the same as absent.
        Assert.Equal(created.RoomId, fetched.Room.Id);
        Assert.Empty(fetched.ClarificationQuestions);
        Assert.Empty(fetched.AgentSteps);
    }

    [Fact]
    public async Task CreateReport_TakesTheReporterFromTheTokenAndIgnoresTheBody()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        // A hand-rolled body carrying a reporterId that is NOT the caller. CreateReportDto
        // has no such property, so this must be ignored rather than honoured — a client
        // must never be able to file a report as somebody else.
        var body = $$"""
            {"description":"Filed by someone pretending to be another user.","roomId":{{roomId}},"reporterId":{{userId + 9999}}}
            """;

        var response = await client.PostAsync(
            "/api/reports", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        Assert.Equal(userId, created!.ReporterId);
    }

    [Fact]
    public async Task CreateReport_WithNoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoomAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Nobody knows who is filing this report.", roomId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateReport_WithUnknownRoom_Returns400NotA500()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Reported against a room that does not exist.", 999_999),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A scanned sticker names the machine: the report carries it from the start, so the
    /// agents can read that asset's history on the first run.
    /// </summary>
    [Fact]
    public async Task CreateReport_WithAnAssetInItsRoom_CarriesTheAsset()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var assetId = await CreateAssetAsync(roomId);

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Projector will not power on at all.", roomId, assetId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        Assert.Equal(assetId, created!.AssetId);

        var detail = await client.GetFromJsonAsync<ReportDetailDto>($"/api/reports/{created.Id}", JsonOptions);
        Assert.Equal(assetId, detail!.Asset!.Id);
    }

    [Fact]
    public async Task CreateReport_WithAnAssetFromAnotherRoom_Returns400_AndFilesNothing()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var elsewhere = await CreateAssetAsync(await CreateRoomAsync());

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Projector will not power on at all.", roomId, elsewhere),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("assetId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var mine = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>("/api/reports", JsonOptions);
        Assert.Equal(0, mine!.TotalCount);
    }

    [Fact]
    public async Task CreateReport_WithAnUnknownAsset_Returns400NotA500()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("Projector will not power on at all.", roomId, 999_999),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateReport_WithTooShortDescription_Returns400()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        // The Flutter client refuses anything under 10 characters. The server enforces the
        // same rule, because a client-side check is a convenience and never a control.
        var response = await client.PostAsJsonAsync(
            "/api/reports", new CreateReportDto("broken", roomId), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateReport_RaisesASubmittedWorkflowAndQueuesIt()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        // The queue is a singleton shared by every test in this class, so drain whatever
        // an earlier test left on it before asserting what this one puts there.
        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();
        await DrainAsync(queue);

        var response = await client.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The air conditioning unit is dripping onto the desks.", roomId),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        // A workflow exists for the report, in Submitted — the runner has not touched it
        // (ApiFactory removes WorkflowRunner), so nothing has moved it on.
        // The workflow list is a manager's and an Admin's read, not the reporter's.
        var workflows = await (await _factory.CreateAdminClientAsync()).GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=50", JsonOptions);

        var workflow = Assert.Single(workflows!.Items.Where(w => w.ReportId == report!.Id));
        Assert.Equal(WorkflowState.Submitted, workflow.CurrentState);
        Assert.Equal(report!.Description, workflow.Objective);

        // And the id was handed to the background runner rather than worked on inline.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var queuedId = await queue.DequeueAsync(timeout.Token);
        Assert.Equal(workflow.Id, queuedId);
    }

    [Fact]
    public async Task CreateReport_WhenItsWorkflowCannotBeSaved_FilesNothing_SoNoReportIsLeftWithoutARun()
    {
        var (_, reporterId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var description = $"Projector flickers, then goes dark {Guid.NewGuid():N}";

        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();
        await DrainAsync(queue);

        // The real ReportService and WorkflowService on ONE context, as DI gives them, with the
        // workflow's insert — the second of the two saves — made to fail.
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var failingDb = ContextWith(sp.GetRequiredService<AppDbContext>(), new FailWorkflowInsert());

            var service = new ReportService(
                failingDb,
                new WorkflowService(failingDb, NullLogger<WorkflowService>.Instance),
                queue,
                sp.GetRequiredService<IClarificationService>(),
                sp.GetRequiredService<IFileStorageService>());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new CreateReportDto(description, roomId), reporterId));
        }

        // The report's own save was rolled back with it: no report that no agent will ever
        // process, and nothing handed to the runner.
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Reports.AnyAsync(r => r.Description == description));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.DequeueAsync(timeout.Token).AsTask());
    }

    // ---------------------------------------------------------------------------------
    // ReportProgress — what a workflow move means for its report, and what the reporter sees
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ReportProgress_TheTriggersThatMoveAReport_AreExactlyThese()
    {
        // Pinned literally, like WorkflowTransitions' table: a changed rule must change this.
        var expected = new[]
        {
            (WorkflowTrigger.Diagnosed, ReportStatus.Diagnosed),
            (WorkflowTrigger.WorkOrderAutoApproved, ReportStatus.WorkOrderRaised),
            (WorkflowTrigger.WorkOrderNeedsApproval, ReportStatus.WorkOrderRaised),
            (WorkflowTrigger.ManagerRejected, ReportStatus.Closed),
            (WorkflowTrigger.RepairVerified, ReportStatus.Closed)
        };

        Assert.Equal(
            expected.OrderBy(p => p.Item1).ToList(),
            ReportProgress.All.Select(p => (p.Trigger, p.To)).OrderBy(p => p.Item1).ToList());

        // And every other trigger moves no report — approval included: the order was raised.
        foreach (var trigger in Enum.GetValues<WorkflowTrigger>().Except(expected.Select(p => p.Item1)))
        {
            Assert.Null(ReportProgress.ImpliedBy(trigger));
        }
    }

    [Theory]
    // A Closed report: rejected reads NotGoingAhead, anything else Closed — whatever the run says.
    [InlineData(ReportStatus.Closed, WorkflowState.Closed, true, ReportStage.NotGoingAhead)]
    [InlineData(ReportStatus.Closed, WorkflowState.Closed, false, ReportStage.Closed)]
    [InlineData(ReportStatus.Closed, WorkflowState.Diagnosing, false, ReportStage.Closed)]
    // Otherwise the latest run decides; a failed or busy run is "being reviewed".
    [InlineData(ReportStatus.Submitted, WorkflowState.Submitted, false, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.AwaitingClarification, WorkflowState.AwaitingClarification, false, ReportStage.WaitingOnYou)]
    [InlineData(ReportStatus.Clarified, WorkflowState.Diagnosing, false, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.Diagnosed, WorkflowState.Strategizing, false, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.Clarified, WorkflowState.Failed, false, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.AwaitingManagerApproval, false, ReportStage.AwaitingApproval)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.WorkOrderRaised, false, ReportStage.RepairPlanned)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.InProgress, false, ReportStage.RepairPlanned)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.Completed, false, ReportStage.Repaired)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.AwaitingVerification, false, ReportStage.Repaired)]
    // A reopened repair is diagnosed again: back to being reviewed.
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.Diagnosing, false, ReportStage.BeingReviewed)]
    // The run ended but the report's own close was skipped by the lifecycle.
    [InlineData(ReportStatus.Clarified, WorkflowState.Closed, true, ReportStage.NotGoingAhead)]
    [InlineData(ReportStatus.WorkOrderRaised, WorkflowState.Closed, false, ReportStage.Repaired)]
    public void ReportProgress_StageFor_ReadsTheReportThenItsLatestRun(
        ReportStatus status, WorkflowState workflow, bool rejected, ReportStage expected)
    {
        Assert.Equal(expected, ReportProgress.StageFor(status, workflow, rejected));
    }

    [Theory]
    [InlineData(ReportStatus.Submitted, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.AwaitingClarification, ReportStage.WaitingOnYou)]
    [InlineData(ReportStatus.Diagnosed, ReportStage.BeingReviewed)]
    [InlineData(ReportStatus.WorkOrderRaised, ReportStage.RepairPlanned)]
    [InlineData(ReportStatus.Closed, ReportStage.Closed)]
    public void ReportProgress_StageFor_AReportWithNoRun_FollowsItsOwnStatus(ReportStatus status, ReportStage expected)
    {
        Assert.Equal(expected, ReportProgress.StageFor(status, latestWorkflow: null, latestOrderRejected: false));
    }

    [Fact]
    public async Task ListAndDetail_CarryTheReportersStage_AndNoCostEstimateOrTechnician()
    {
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "Stage check: the lectern microphone is dead.");

        var page = await reporter.GetFromJsonAsync<JsonElement>("/api/reports?page=1&pageSize=100", JsonOptions);
        var row = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == reportId);
        var detail = await reporter.GetFromJsonAsync<JsonElement>($"/api/reports/{reportId}", JsonOptions);

        // By NAME, and the same answer in both places.
        Assert.Equal("BeingReviewed", row.GetProperty("stage").GetString());
        Assert.Equal("BeingReviewed", detail.GetProperty("stage").GetString());

        // What a reporter reads about progress says nothing about money or who is sent.
        var names = row.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.DoesNotContain(names, n => n.Contains("cost") || n.Contains("estimate") || n.Contains("technician"));
    }

    [Fact]
    public async Task StartWorkflow_WithAReportIdThatDoesNotExist_Returns400NotA500()
    {
        // ReportId is a real foreign key now, so an unchecked bad id would surface as a
        // constraint violation out of the driver instead of a validation failure.
        var (client, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        var response = await client.PostAsJsonAsync(
            "/api/workflows",
            new StartWorkflowRequest("Investigate a report that was never filed.", 999_999),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Empties the queue. DequeueAsync blocks, so a short timeout is the signal.</summary>
    private static async Task DrainAsync(IWorkflowQueue queue)
    {
        while (true)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            try
            {
                await queue.DequeueAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task GetReport_WithUnknownId_Returns404()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // GET /api/reports — the list, and who is allowed to see what.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListReports_ScopesAReporterToTheirOwnAndShowsAManagerEverything()
    {
        // THE RULE THIS ENDPOINT EXISTS TO ENFORCE, and it is enforced in the service: there
        // is no query parameter below that widens it and nothing a client can send.
        var (alice, aliceId) = await CreateAuthenticatedClientAsync();
        var (bob, bobId) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var aliceReport = await FileReportAsync(alice, roomId, "Alice: the projector flickers badly.");
        var bobReport = await FileReportAsync(bob, roomId, "Bob: the door lock jams every morning.");

        var aliceList = await alice.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?pageSize=100", JsonOptions);

        Assert.All(aliceList!.Items, r => Assert.Equal(aliceId, r.ReporterId));
        Assert.Contains(aliceList.Items, r => r.Id == aliceReport);
        Assert.DoesNotContain(aliceList.Items, r => r.Id == bobReport);

        // TotalCount describes the scoped set, not the table — the scope is applied before
        // the count, so a Reporter cannot learn how many reports exist by reading it.
        Assert.Equal(aliceList.Items.Count, aliceList.TotalCount);

        var bobList = await bob.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?pageSize=100", JsonOptions);

        Assert.All(bobList!.Items, r => Assert.Equal(bobId, r.ReporterId));
        Assert.DoesNotContain(bobList.Items, r => r.Id == aliceReport);

        // A manager sees the estate.
        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);
        var managerList = await manager.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?pageSize=100", JsonOptions);

        Assert.Contains(managerList!.Items, r => r.Id == aliceReport);
        Assert.Contains(managerList.Items, r => r.Id == bobReport);
    }

    [Fact]
    public async Task ListReports_ScopesATechnicianToo_BecauseTheRuleFailsClosed()
    {
        // Only FacilitiesManager and Admin are named as seeing everything, so every other
        // role — including one added to the enum later — is scoped to its own reports until
        // somebody deliberately widens it.
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var theirs = await FileReportAsync(reporter, roomId, "A fault only the reporter filed.");

        var (technician, _) = await CreateAuthenticatedClientAsync(Role.Technician);

        var list = await technician.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?pageSize=100", JsonOptions);

        Assert.DoesNotContain(list!.Items, r => r.Id == theirs);
    }

    [Fact]
    public async Task ListReports_WithoutAToken_Is401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListReports_SearchesTheDescriptionCaseInsensitively()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var wanted = await FileReportAsync(client, roomId, "The CENTRIFUGE is vibrating loudly.");
        var other = await FileReportAsync(client, roomId, "The whiteboard marker tray is broken.");

        // Lower-cased on both sides, so SQLite and PostgreSQL agree — SQLite's LIKE is
        // already case-insensitive and PostgreSQL's is not.
        var list = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?search=centrifuge&pageSize=100", JsonOptions);

        Assert.Contains(list!.Items, r => r.Id == wanted);
        Assert.DoesNotContain(list.Items, r => r.Id == other);
    }

    [Fact]
    public async Task ListReports_FiltersByStatusRoomAndAssetAndTheyCombine()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomA = await CreateRoomAsync();
        var roomB = await CreateRoomAsync();

        var target = await FileReportAsync(client, roomA, "The extractor fan has stopped turning.");
        var wrongRoom = await FileReportAsync(client, roomB, "The extractor fan has stopped turning.");
        var wrongStatus = await FileReportAsync(client, roomA, "Another fault in the same room.");

        var assetId = await CreateAssetAsync(roomA);

        // Status and AssetId are not settable through the create DTO, by design, so they are
        // arranged directly.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Reports.FirstAsync(r => r.Id == target);
            row.Status = ReportStatus.Diagnosed;
            row.AssetId = assetId;
            await db.SaveChangesAsync();
        }

        var byStatus = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            $"/api/reports?status=Diagnosed&roomId={roomA}&assetId={assetId}&pageSize=100", JsonOptions);

        var only = Assert.Single(byStatus!.Items);
        Assert.Equal(target, only.Id);
        Assert.NotEqual(target, wrongRoom);
        Assert.NotEqual(target, wrongStatus);

        // The room name is denormalised onto the row, so a table renders without a query per
        // line.
        Assert.False(string.IsNullOrWhiteSpace(only.RoomName));
    }

    [Fact]
    public async Task ListReports_WithAStatusThatIsNotAMember_Is400FromModelBinding()
    {
        // Bound by NAME. An unknown value is a refusal, never a filter that silently
        // matches nothing.
        var (client, _) = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports?status=Exploded");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListReports_FiltersByDateRangeWithBothEndsInclusive()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var old = await FileReportAsync(client, roomId, "Filed a fortnight ago, long fixed.");
        var recent = await FileReportAsync(client, roomId, "Filed this morning, still broken.");

        var cutoff = DateTime.UtcNow.AddDays(-14);

        // Backdated with SQL, not through the DbContext: ApplyTimestamps deliberately marks
        // CreatedAt unmodifiable on an update, so an assignment here would be discarded
        // silently. A report really filed a fortnight ago is what this is standing in for.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Reports"" SET ""CreatedAt"" = {0} WHERE ""Id"" = {1}", cutoff, old);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldDay = DateOnly.FromDateTime(cutoff);

        // dateFrom today catches the recent one and excludes the fortnight-old one.
        var fromToday = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            $"/api/reports?dateFrom={today:yyyy-MM-dd}&pageSize=100", JsonOptions);

        Assert.Contains(fromToday!.Items, r => r.Id == recent);
        Assert.DoesNotContain(fromToday.Items, r => r.Id == old);

        // THE INCLUSIVE END. dateTo is the old report's own day: a naive "<= midnight" would
        // drop it, because it was filed during that day and not at the start of it.
        var toThatDay = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            $"/api/reports?dateTo={oldDay:yyyy-MM-dd}&pageSize=100", JsonOptions);

        Assert.Contains(toThatDay!.Items, r => r.Id == old);
        Assert.DoesNotContain(toThatDay.Items, r => r.Id == recent);
    }

    [Fact]
    public async Task ListReports_SortsNewestFirstByDefaultAndPagesWithATotalOrder()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        for (var i = 0; i < 5; i++)
        {
            await FileReportAsync(client, roomId, $"Sequentially filed fault number {i}.");
        }

        var page1 = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?page=1&pageSize=2", JsonOptions);

        Assert.Equal(2, page1!.Items.Count);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);

        // A worklist is read from the top, so the default is newest first — the opposite of
        // the asset registry's alphabetical default, and for the opposite reason.
        Assert.True(page1.Items[0].CreatedAt >= page1.Items[1].CreatedAt);

        var page2 = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?page=2&pageSize=2", JsonOptions);

        // Every sort carries a tiebreak on Id, so no row appears on two pages or on neither
        // — reports share a CreatedAt readily, since it is stamped per SaveChanges.
        Assert.Empty(page1.Items.Select(r => r.Id).Intersect(page2!.Items.Select(r => r.Id)));
    }

    [Fact]
    public async Task ListReports_SortByStatusGroupsTheLifecycleTogether()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();

        var a = await FileReportAsync(client, roomId, "One that will be marked closed.");
        await FileReportAsync(client, roomId, "One that stays submitted.");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Reports.FirstAsync(r => r.Id == a);
            row.Status = ReportStatus.Closed;
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            "/api/reports?sort=Status&pageSize=100", JsonOptions);

        // Ordered on the stored STRING, so this is alphabetical rather than lifecycle order
        // — Closed before Submitted. That is a consequence of storing enums by name, and
        // grouping is what a caller sorting by status is after.
        var statuses = list!.Items.Select(r => r.Status.ToString()).ToList();
        Assert.Equal(statuses.OrderBy(x => x, StringComparer.Ordinal), statuses);
    }

    [Fact]
    public async Task ListReports_CountsUnansweredClarificationQuestionsWithoutFetchingThem()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(client, roomId, "A fault the clarifier will ask about.");

        var workflowId = await WorkflowIdForAsync(reportId);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IClarificationService>()
                .RecordQuestionsAsync(reportId, workflowId, new[]
                {
                    new ParsedClarifyingQuestion("Is it completely dead?", AnswerType.YesNo, null, 0),
                    new ParsedClarifyingQuestion("Since when?", AnswerType.SingleSelect,
                        """["Today","This week"]""", 1)
                });
        }

        var list = await client.GetFromJsonAsync<PagedResult<ReportListItemDto>>(
            $"/api/reports?roomId={roomId}&pageSize=100", JsonOptions);

        var row = Assert.Single(list!.Items.Where(r => r.Id == reportId));

        // The one thing a list needs to say about clarification — "this is waiting on you".
        Assert.Equal(2, row.UnansweredQuestionCount);
    }

    // ---------------------------------------------------------------------------
    // GET /api/reports/{id} — the detail read.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetReport_CarriesTheClarificationQuestionsAndTheAgentSteps()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(client, roomId, "The lift makes a grinding noise.");
        var workflowId = await WorkflowIdForAsync(reportId);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IClarificationService>()
                .RecordQuestionsAsync(reportId, workflowId, new[]
                {
                    new ParsedClarifyingQuestion("Is it between floors?", AnswerType.YesNo, null, 0)
                });

            await scope.ServiceProvider.GetRequiredService<IWorkflowService>()
                .RecordStepAsync(workflowId, "clarifier", "[]", """{"questions":[]}""",
                    1200, "Ok", null);
        }

        var detail = await client.GetFromJsonAsync<ReportDetailDto>(
            $"/api/reports/{reportId}", JsonOptions);

        Assert.Equal(roomId, detail!.Room.Id);
        Assert.Null(detail.Asset);

        var question = Assert.Single(detail.ClarificationQuestions);
        Assert.Equal("Is it between floors?", question.QuestionText);

        // The audit trail, shown as recorded and never edited.
        var step = Assert.Single(detail.AgentSteps);
        Assert.Equal("clarifier", step.AgentName);
        Assert.Equal(workflowId, step.WorkflowId);
        Assert.Equal("Ok", step.ValidationResult);
    }

    [Fact]
    public async Task GetReport_BelongingToSomebodyElse_Is403ForAReporterAnd200ForAManager()
    {
        // A list that hides other people's reports while a detail read hands them over by id
        // would be a rule that only looks enforced.
        var (alice, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var aliceReport = await FileReportAsync(alice, roomId, "Alice's own fault report.");

        var (bob, _) = await CreateAuthenticatedClientAsync();

        var refused = await bob.GetAsync($"/api/reports/{aliceReport}");

        // 403, not 404: the token is valid and we know exactly who is asking. This matches
        // POST {id}/clarifications on the same resource.
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);
        var allowed = await manager.GetAsync($"/api/reports/{aliceReport}");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // PATCH /api/reports/{id}/status — the lifecycle.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PatchStatus_AsAManager_MovesTheReportAlongAndReturns204()
    {
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "A fault a manager will triage.");

        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        // Submitted may jump straight to Diagnosed: clarification is what the agent asks for
        // when it needs more detail, and a clear report does not need it.
        var response = await manager.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status",
            new UpdateReportStatusDto(ReportStatus.Diagnosed), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);

        Assert.Equal(ReportStatus.Diagnosed, row.Status);
    }

    [Fact]
    public async Task PatchStatus_RejectsAnIllegalTransitionWith409AndChangesNothing()
    {
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "A fault nobody has diagnosed.");

        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        // Submitted straight to WorkOrderRaised skips diagnosis entirely. Refused, not
        // quietly written — a status that could go anywhere is not a lifecycle.
        var response = await manager.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status",
            new UpdateReportStatusDto(ReportStatus.WorkOrderRaised), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // 409, not 400: the value is a real member of the enum and nothing about the request
        // is malformed. It is the report that is not where the caller thinks it is.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(JsonOptions);
        Assert.Equal("Illegal status transition", problem!.Title);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);

        Assert.Equal(ReportStatus.Submitted, row.Status);
    }

    [Fact]
    public async Task PatchStatus_ToTheStatusItIsAlreadyIn_Is409BecauseThatIsNotATransition()
    {
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "A fault that is staying put.");

        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        var response = await manager.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status",
            new UpdateReportStatusDto(ReportStatus.Submitted), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_CannotReopenAClosedReport()
    {
        // Closed is terminal and there is deliberately no way back. A fault that returns is
        // a new report with its own history — the same rule the verification loop follows
        // when a failed repair produces a NEW work order rather than reusing the row.
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "A fault about to be closed.");

        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        var closed = await manager.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status",
            new UpdateReportStatusDto(ReportStatus.Closed), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        foreach (var attempt in new[]
                 {
                     ReportStatus.Submitted, ReportStatus.Diagnosed, ReportStatus.WorkOrderRaised
                 })
        {
            var reopen = await manager.PatchAsJsonAsync(
                $"/api/reports/{reportId}/status",
                new UpdateReportStatusDto(attempt), JsonOptions);

            Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
        }
    }

    [Fact]
    public async Task PatchStatus_IsRefusedForEveryoneButAFacilitiesManager()
    {
        var (reporter, _) = await CreateAuthenticatedClientAsync();
        var roomId = await CreateRoomAsync();
        var reportId = await FileReportAsync(reporter, roomId, "A fault only a manager may move.");

        var body = new UpdateReportStatusDto(ReportStatus.Diagnosed);

        // No token is 401 — "who are you?" — and a valid token with the wrong role is 403.
        // Both are required and they stay distinct.
        var anonymous = await _factory.CreateClient()
            .PatchAsJsonAsync($"/api/reports/{reportId}/status", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var byReporter = await reporter.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, byReporter.StatusCode);

        var (technician, _) = await CreateAuthenticatedClientAsync(Role.Technician);
        var byTechnician = await technician.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, byTechnician.StatusCode);

        // AN ADMIN IS REFUSED TOO, and that is worth pinning because it surprises people.
        // The policy is one-per-role and names FacilitiesManager, so it does not fall back
        // to "or anyone more senior" — there is no seniority ordering on Role, and inventing
        // one here would put a second, implicit authorisation rule beside the explicit one.
        // Moving a fault along its lifecycle is a facilities decision, not an estate one.
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var byAdmin = await admin.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status", body, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_OnAReportThatDoesNotExist_Is404()
    {
        var (manager, _) = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        var response = await manager.PatchAsJsonAsync(
            "/api/reports/999999/status",
            new UpdateReportStatusDto(ReportStatus.Diagnosed), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Just the fields these tests read off a ProblemDetails body.</summary>
    private record ProblemDetailsBody(string? Title, int? Status, string? Detail);

    /// <summary>Files a report through the API and returns its id.</summary>
    private static async Task<int> FileReportAsync(HttpClient client, int roomId, string description)
    {
        var response = await client.PostAsJsonAsync(
            "/api/reports", new CreateReportDto(description, roomId), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        return report!.Id;
    }

    /// <summary>The workflow POST /api/reports raised for a report.</summary>
    private async Task<int> WorkflowIdForAsync(int reportId)
    {
        // The workflow list is a manager's and an Admin's read, not the reporter's.
        var workflows = await (await _factory.CreateAdminClientAsync()).GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
            "/api/workflows?page=1&pageSize=100", JsonOptions);

        return Assert.Single(workflows!.Items.Where(w => w.ReportId == reportId)).Id;
    }

    /// <summary>
    /// A second context on the same database, with an interceptor — the same helper as
    /// VerificationSweepTests. SQLite shares the held-open connection; PostgreSQL reconnects.
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

    /// <summary>Stands in for the database refusing the workflow's insert, after the report's has succeeded.</summary>
    private sealed class FailWorkflowInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var addingWorkflow = eventData.Context!.ChangeTracker.Entries<AgentWorkflow>()
                .Any(e => e.State == EntityState.Added);

            return addingWorkflow
                ? throw new InvalidOperationException("Simulated failure saving the workflow.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Registers an asset in a room, so the assetId filter has something to match.</summary>
    private async Task<int> CreateAssetAsync(int roomId)
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);

        var categoryResponse = await admin.PostAsJsonAsync(
            "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions);
        var category = await categoryResponse.Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var assetResponse = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(
                UniqueCode(), "Ceiling Projector", category!.Id, roomId, "Acme", "X1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)), null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        return asset!.Id;
    }
}
