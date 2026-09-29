using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace api.Tests;

public class WorkflowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkflowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // The API serialises enums by name, so the tests must too — otherwise these tests
    // would pass against a contract the real clients cannot actually use.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    /// <summary>Returns a client already carrying a bearer token for a fresh account.</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(Role role = Role.FacilitiesManager)
    {
        var client = _factory.CreateClient();

        var response = await _factory.RegisterAsync(new RegisterRequest(UniqueEmail(), "WorkflowPass1", "Test User", role));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }

    /// <summary>Client for the agent service: no JWT, just the shared secret header.</summary>
    private HttpClient CreateAgentClient(bool withSecret = true)
    {
        var client = _factory.CreateClient();

        if (withSecret)
        {
            client.DefaultRequestHeaders.Add("X-Agent-Secret", ApiFactory.AgentSharedSecret);
        }

        return client;
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

    private async Task<WorkflowSummaryDto> StartWorkflowAsync(HttpClient client, string objective)
    {
        var response = await client.PostAsJsonAsync(
            "/api/workflows", new StartWorkflowRequest(objective), JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<WorkflowSummaryDto>(JsonOptions);
        Assert.NotNull(created);
        return created!;
    }

    // -----------------------------------------------------------------------
    // POST /api/workflows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StartWorkflow_Returns202AndPersistsTheRow()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/workflows",
            new StartWorkflowRequest("Projector in B204 will not power on"), JsonOptions);

        // 202, not 201: the row exists but the work it describes has not happened yet.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<WorkflowSummaryDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal(WorkflowState.Submitted, created.CurrentState);

        // The Location header points at the endpoint the client is expected to poll.
        Assert.NotNull(response.Headers.Location);

        // Persisted, not just echoed back: read it again through a separate request.
        var detailResponse = await client.GetAsync($"/api/workflows/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkflowDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail!.Id);
        Assert.Equal("Projector in B204 will not power on", detail.Objective);
    }

    [Fact]
    public async Task StartWorkflow_QueuesTheIdInsteadOfDoingTheWorkInline()
    {
        var client = await CreateAuthenticatedClientAsync();

        // The runner is removed in tests, so nothing consumes the queue and every other
        // test in this class has left its id on it. Drain first, so what comes off next
        // is this test's workflow and not the oldest one.
        var queue = _factory.Services.GetRequiredService<IWorkflowQueue>();
        await DrainAsync(queue);

        var created = await StartWorkflowAsync(client, "Aircon leaking in the library");

        // The POST returned without waiting for anything, and left the id on the queue
        // for the background runner to pick up.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queuedId = await queue.DequeueAsync(timeout.Token);

        Assert.Equal(created.Id, queuedId);
    }

    [Fact]
    public async Task StartWorkflow_WithNoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/workflows", new StartWorkflowRequest("Anonymous request"), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // GET /api/workflows
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetWorkflow_WithUnknownId_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/workflows/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkflows_FiltersByStateAndPaginates()
    {
        var client = await CreateAuthenticatedClientAsync();
        await StartWorkflowAsync(client, "Filterable workflow");

        var response = await client.GetAsync("/api/workflows?state=Submitted&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<WorkflowSummaryDto>>(JsonOptions);

        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Single(page.Items);
        Assert.All(page.Items, w => Assert.Equal(WorkflowState.Submitted, w.CurrentState));

        // Nothing is in this state, so the filter must genuinely filter.
        var emptyResponse = await client.GetAsync("/api/workflows?state=Closed");
        var emptyPage = await emptyResponse.Content
            .ReadFromJsonAsync<PagedResult<WorkflowSummaryDto>>(JsonOptions);

        Assert.Empty(emptyPage!.Items);
        Assert.Equal(0, emptyPage.TotalCount);
    }

    // -----------------------------------------------------------------------
    // POST /api/internal/tools/{toolName}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ToolCall_WithNoSecret_Returns401()
    {
        var client = CreateAgentClient(withSecret: false);

        var response = await client.PostAsJsonAsync(
            "/api/internal/tools/get_room", new ToolCallRequest(1, 1, "diagnostician"), JsonOptions);

        // Not 403 and not 400: an unauthenticated caller is told who they are not, and
        // learns nothing about whether the tool or the body was valid.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToolCall_WithWrongSecret_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "not-the-right-secret");

        var response = await client.PostAsJsonAsync(
            "/api/internal/tools/get_room", new ToolCallRequest(1, 1, "diagnostician"), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToolCall_WithNameNotInTheAllowList_Returns404AndLogsAWarning()
    {
        var userClient = await CreateAuthenticatedClientAsync();
        var workflow = await StartWorkflowAsync(userClient, "Workflow for a rejected tool call");

        var agentClient = CreateAgentClient();

        // A name the agent might plausibly invent, or be talked into asking for.
        var response = await agentClient.PostAsJsonAsync(
            "/api/internal/tools/delete_all_rooms",
            new ToolCallRequest(workflow.Id, 1, "diagnostician"), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The rejection is logged as a warning — that log line is how anyone would notice
        // an agent reaching for something it was never granted.
        var warning = _factory.Logs.Entries.FirstOrDefault(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("delete_all_rooms"));

        Assert.NotNull(warning);
        Assert.Contains("allow-list", warning!.Message);

        // And it is on the workflow's audit trail, not only in the log sink.
        var detail = await userClient.GetFromJsonAsync<WorkflowDetailDto>(
            $"/api/workflows/{workflow.Id}", JsonOptions);

        var rejectedStep = Assert.Single(detail!.Steps);
        Assert.Equal("RejectedUnknownTool", rejectedStep.ValidationResult);
        Assert.Contains("delete_all_rooms", rejectedStep.ToolCallsJson!);
    }

    [Fact]
    public async Task ToolCall_GetRoom_ReturnsTheRoomAndWritesAnAgentStep()
    {
        var userClient = await CreateAuthenticatedClientAsync();

        // Buildings and rooms are an Admin's to create.
        var admin = await _factory.CreateAdminClientAsync();

        var buildingResponse = await admin.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Science Block", $"SB{Guid.NewGuid():N}"[..8]), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, buildingResponse.StatusCode);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await admin.PostAsJsonAsync(
            "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall 204", "B204", 2), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var workflow = await StartWorkflowAsync(userClient, "Look up B204");

        var agentClient = CreateAgentClient();
        var response = await agentClient.PostAsJsonAsync(
            "/api/internal/tools/get_room",
            new ToolCallRequest(workflow.Id, room!.Id, "diagnostician"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("get_room", body.GetProperty("tool").GetString());
        Assert.True(body.GetProperty("found").GetBoolean());
        Assert.Equal("B204", body.GetProperty("result").GetProperty("code").GetString());

        // Every tool call writes an AgentStep — that is the audit trail.
        var detail = await userClient.GetFromJsonAsync<WorkflowDetailDto>(
            $"/api/workflows/{workflow.Id}", JsonOptions);

        var step = Assert.Single(detail!.Steps);
        Assert.Equal("diagnostician", step.AgentName);
        Assert.Equal("Ok", step.ValidationResult);
        Assert.Contains("get_room", step.ToolCallsJson!);
        Assert.Contains("B204", step.PayloadJson!);
    }

    [Fact]
    public async Task ToolCall_GetBuilding_ForAMissingId_Returns200WithFoundFalse()
    {
        var userClient = await CreateAuthenticatedClientAsync();
        var workflow = await StartWorkflowAsync(userClient, "Look up a building that is not there");

        var agentClient = CreateAgentClient();
        var response = await agentClient.PostAsJsonAsync(
            "/api/internal/tools/get_building",
            new ToolCallRequest(workflow.Id, 999999, "diagnostician"), JsonOptions);

        // 200 with found=false, not 404: the tool exists and ran. Reserving 404 for
        // "no such tool" is what keeps an allow-list rejection visible.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task ToolCall_ForAWorkflowThatDoesNotExist_Returns400()
    {
        var agentClient = CreateAgentClient();

        var response = await agentClient.PostAsJsonAsync(
            "/api/internal/tools/get_room",
            new ToolCallRequest(999999, 1, "diagnostician"), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ToolCall_WithAnIdThatCanNameNoRow_Is400()
    {
        var workflow = await StartWorkflowAsync(await CreateAuthenticatedClientAsync(), "Look up nothing");

        var response = await CreateAgentClient().PostAsJsonAsync(
            "/api/internal/tools/get_room", new ToolCallRequest(workflow.Id, 0, "clarifier"), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The case this exists for: the API's wait on /run timed out and marked the run Failed
    /// while the agent was still working. Its later tool calls are refused, and nothing more is
    /// written onto a workflow already declared dead.
    /// </summary>
    [Fact]
    public async Task ToolCall_ForAWorkflowThatHasEnded_Is409_AndWritesNothing()
    {
        var manager = await CreateAuthenticatedClientAsync();
        var workflow = await StartWorkflowAsync(manager, "A run the API has given up on");

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWorkflowService>()
                .FailAsync(workflow.Id, "The agent service did not respond in time.");
        }

        var response = await CreateAgentClient().PostAsJsonAsync(
            "/api/internal/tools/get_building", new ToolCallRequest(workflow.Id, 1, "clarifier"), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkflowDetailDto>($"/api/workflows/{workflow.Id}", JsonOptions);
        Assert.Empty(detail!.Steps);
    }

    // -----------------------------------------------------------------------------------
    // Who oversees the agent workflows: a FacilitiesManager and an Admin. A workflow's
    // objective is a reporter's own words and its steps everything the agents said about the
    // report, so a Reporter follows their report through GET /api/reports instead.
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    public async Task Workflows_AreNotAReportersOrATechniciansToListReadOrStart(Role role)
    {
        var workflow = await StartWorkflowAsync(await CreateAuthenticatedClientAsync(), "Someone else's fault");
        var client = await CreateAuthenticatedClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workflows")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/workflows/{workflow.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest("Run the agents"), JsonOptions)).StatusCode);
    }

    [Theory]
    [InlineData(Role.FacilitiesManager)]
    [InlineData(Role.Admin)]
    public async Task Workflows_AreAManagersAndAnAdminsToListReadAndStart(Role role)
    {
        var client = await CreateAuthenticatedClientAsync(role);
        var workflow = await StartWorkflowAsync(client, "Run the agents");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workflows")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/workflows/{workflow.Id}")).StatusCode);
    }

    [Fact]
    public async Task StartWorkflow_ForAClosedReport_Is409()
    {
        var manager = await CreateAuthenticatedClientAsync();
        var reportId = await FileReportAsync();

        var closed = await manager.PatchAsJsonAsync(
            $"/api/reports/{reportId}/status", new UpdateReportStatusDto(ReportStatus.Closed), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        var response = await manager.PostAsJsonAsync(
            "/api/workflows", new StartWorkflowRequest("Run it again", reportId), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// One report, one live run: every later action moves the report's LATEST workflow, so a
    /// second run beside a live one would quietly take its place. Once the run has ended, a
    /// manager may run the agents again.
    /// </summary>
    [Fact]
    public async Task StartWorkflow_WhileTheReportsRunIsLive_Is409_ButOnceItHasFailed_ANewRunStarts()
    {
        var manager = await CreateAuthenticatedClientAsync();
        var reportId = await FileReportAsync();

        var whileLive = await manager.PostAsJsonAsync(
            "/api/workflows", new StartWorkflowRequest("Run it again", reportId), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, whileLive.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var workflowId = await scope.ServiceProvider.GetRequiredService<CampusFacilities.Api.Data.AppDbContext>()
                .AgentWorkflows.Where(w => w.ReportId == reportId).Select(w => w.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<IWorkflowService>().FailAsync(workflowId, "Agent down.");
        }

        var afterFailure = await manager.PostAsJsonAsync(
            "/api/workflows", new StartWorkflowRequest("Run it again", reportId), JsonOptions);
        Assert.Equal(HttpStatusCode.Accepted, afterFailure.StatusCode);
    }

    /// <summary>A report filed by a fresh Reporter in a fresh room; POST /api/reports starts its run.</summary>
    private async Task<int> FileReportAsync()
    {
        var admin = await _factory.CreateAdminClientAsync();

        var building = await (await admin.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Science Block", $"SB{Guid.NewGuid():N}"[..8]), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);
        var room = await (await admin.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lab 3", $"L{Guid.NewGuid():N}"[..8], 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var reporter = await CreateAuthenticatedClientAsync(Role.Reporter);
        var report = await reporter.PostAsJsonAsync(
            "/api/reports", new CreateReportDto("The lab fume cupboard fan has stopped.", room!.Id), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, report.StatusCode);

        return (await report.Content.ReadFromJsonAsync<ReportDto>(JsonOptions))!.Id;
    }
}
