using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.Extensions.DependencyInjection;

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

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "ReportPass1", "Test Reporter", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>Creates a building and a room in it, returning the room's id.</summary>
    private async Task<int> CreateRoomAsync()
    {
        var client = _factory.CreateClient();

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

        var fetched = await client.GetFromJsonAsync<ReportDto>(location, JsonOptions);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.Description, fetched.Description);
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
        var workflows = await client.GetFromJsonAsync<PagedResult<WorkflowSummaryDto>>(
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
}
