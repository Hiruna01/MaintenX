using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
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

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "WorkflowPass1", "Test User", role), JsonOptions);

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

        var buildingResponse = await userClient.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Science Block", $"SB{Guid.NewGuid():N}"[..8]), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, buildingResponse.StatusCode);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await userClient.PostAsJsonAsync(
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
}
