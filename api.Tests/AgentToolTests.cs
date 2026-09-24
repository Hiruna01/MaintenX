using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// The asset tools on the agent's allow-list. What is pinned here is not that they
/// return rows — it is the properties that make them safe to hand to a model:
///
///   * THE CAPS ARE NOT NEGOTIABLE. ToolCallRequest has no limit field, so a caller
///     cannot widen them, and the order is newest-first BECAUSE of the cap — the twenty
///     oldest visits would be the twenty least useful.
///   * NULL AND EMPTY ARE DIFFERENT ANSWERS. An unknown asset is found=false; an asset
///     with nothing against it is found=true with an empty list. Collapsing those would
///     tell the agent a machine has a clean record when it asked about a machine that
///     does not exist.
///   * EVERY CALL IS AUDITED, including rejections, and nothing on the list returns a
///     judgement.
/// </summary>
public class AgentToolTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AgentToolTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    public async Task GetAsset_ReturnsTheAssetWithItsCategoryAndRoomNames_AndWritesAnAgentStep()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin, "Lecture Hall A Projector", "Projectors", "Lecture Hall A");
        var workflow = await StartWorkflowAsync(admin);

        var body = await CallToolAsync("get_asset", workflow.Id, asset.Id);

        Assert.Equal("get_asset", body.GetProperty("tool").GetString());
        Assert.True(body.GetProperty("found").GetBoolean());

        var result = body.GetProperty("result");
        Assert.Equal(asset.AssetTag, result.GetProperty("asset").GetProperty("assetTag").GetString());

        // The two names are the whole reason this is not just AssetDto: they are what
        // lets the agent talk about the machine without a second and third tool call.
        Assert.Equal("Projectors", result.GetProperty("categoryName").GetString());
        Assert.Equal("Lecture Hall A", result.GetProperty("roomName").GetString());

        // No service history on this payload — that is a separate tool, with a cap.
        Assert.False(result.TryGetProperty("serviceHistory", out _));

        // Status is a NAME here too, like everywhere else.
        Assert.Equal("Active", result.GetProperty("asset").GetProperty("status").GetString());

        var step = await SingleStepAsync(admin, workflow.Id);
        Assert.Equal("Ok", step.ValidationResult);
        Assert.Contains("get_asset", step.ToolCallsJson!);
        Assert.Contains(asset.AssetTag, step.PayloadJson!);
    }

    [Fact]
    public async Task GetAsset_ForAnUnknownId_Returns200WithFoundFalse()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var workflow = await StartWorkflowAsync(admin);

        var body = await CallToolAsync("get_asset", workflow.Id, 999999);

        // 200, not 404: the tool exists and ran. 404 stays reserved for "no such tool",
        // which is what keeps an allow-list rejection visible.
        Assert.False(body.GetProperty("found").GetBoolean());

        var step = await SingleStepAsync(admin, workflow.Id);
        Assert.Equal("NotFound", step.ValidationResult);
    }

    [Fact]
    public async Task GetAssetServiceHistory_IsNewestFirst_AndCappedAtTwentyRows()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);

        // More visits than the cap, on distinct dates, oldest first as they happened.
        var visits = Enumerable.Range(0, 25)
            .Select(i => new DateOnly(2024, 1, 1).AddDays(i * 7))
            .ToList();

        await SeedHistoryAsync(asset.Id, visits);

        var body = await CallToolAsync("get_asset_service_history", workflow.Id, asset.Id);

        Assert.True(body.GetProperty("found").GetBoolean());

        var rows = body.GetProperty("result").EnumerateArray().ToList();
        Assert.Equal(IAssetService.MaxToolHistoryRows, rows.Count);

        var returned = rows
            .Select(r => DateOnly.Parse(r.GetProperty("servicedOn").GetString()!))
            .ToList();

        // Newest first — the opposite of the detail read, and deliberately so: the cap
        // means an oldest-first list would hand back the twenty LEAST relevant visits and
        // hide every recent one.
        Assert.Equal(returned.OrderByDescending(d => d), returned);
        Assert.Equal(visits.Max(), returned.First());
        Assert.All(returned, d => Assert.Contains(d, visits));

        // The five oldest visits are the ones dropped, not the five newest.
        Assert.DoesNotContain(visits.Min(), returned);
    }

    [Fact]
    public async Task GetAssetServiceHistory_TellsAnUnknownAssetApartFromOneNeverServiced()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);

        // Exists, never serviced: found=true and an empty list. That is an ANSWER.
        var untouched = await CallToolAsync("get_asset_service_history", workflow.Id, asset.Id);
        Assert.True(untouched.GetProperty("found").GetBoolean());
        Assert.Empty(untouched.GetProperty("result").EnumerateArray());

        // Does not exist: found=false. If this collapsed into the case above, the agent
        // would read "clean service record" off an asset that is not there.
        var unknown = await CallToolAsync("get_asset_service_history", workflow.Id, 999999);
        Assert.False(unknown.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task GetRelatedOpenReports_ExcludesClosedOnes_AndCapsAtTen()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var (_, reporterId) = await CreateAuthenticatedClientAsync(Role.Reporter);

        var asset = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);

        // Twelve open reports and two closed ones against the same machine.
        var openIds = await SeedReportsAsync(asset, reporterId, 12, ReportStatus.Submitted);
        await SeedReportsAsync(asset, reporterId, 2, ReportStatus.Closed);

        var body = await CallToolAsync("get_related_open_reports", workflow.Id, asset.Id);

        Assert.True(body.GetProperty("found").GetBoolean());

        var rows = body.GetProperty("result").EnumerateArray().ToList();
        Assert.Equal(IReportService.MaxToolRelatedReports, rows.Count);

        // Nothing Closed comes back: the question is "is anyone else seeing this now",
        // and a fault closed last year belongs in the service history instead.
        Assert.All(rows, r => Assert.NotEqual("Closed", r.GetProperty("status").GetString()));

        // Newest first, so the cap drops the two oldest rather than today's complaint.
        var returnedIds = rows.Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(returnedIds.OrderByDescending(i => i), returnedIds);
        Assert.Equal(openIds.Max(), returnedIds.First());
        Assert.DoesNotContain(openIds.Min(), returnedIds);
    }

    [Fact]
    public async Task GetRelatedOpenReports_TellsAnUnknownAssetApartFromOneWithNothingOpen()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);

        var quiet = await CallToolAsync("get_related_open_reports", workflow.Id, asset.Id);
        Assert.True(quiet.GetProperty("found").GetBoolean());
        Assert.Empty(quiet.GetProperty("result").EnumerateArray());

        var unknown = await CallToolAsync("get_related_open_reports", workflow.Id, 999999);
        Assert.False(unknown.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task TheAllowListHasNoJudgementTool()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var workflow = await StartWorkflowAsync(admin);
        var agentClient = CreateAgentClient();

        // Tools return facts. A "diagnose" or "recommend" tool would hand the model its
        // own opinion back wearing this API's authority — so these names are not in the
        // dictionary, and a request for one is rejected like any other unknown tool.
        foreach (var name in new[] { "diagnose", "diagnose_asset", "recommend_action", "assess_risk" })
        {
            var response = await agentClient.PostAsJsonAsync(
                $"/api/internal/tools/{name}",
                new ToolCallRequest(workflow.Id, 1, "diagnostician"), JsonOptions);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // Every rejection is on the audit trail, not only in the log sink.
        var detail = await admin.GetFromJsonAsync<WorkflowDetailDto>(
            $"/api/workflows/{workflow.Id}", JsonOptions);

        Assert.Equal(4, detail!.Steps.Count);
        Assert.All(detail.Steps, s => Assert.Equal("RejectedUnknownTool", s.ValidationResult));
    }

    [Fact]
    public async Task GetOpenWorkOrders_ReturnsOpenOrdersInTheAssetsRoom_NewestFirst_Capped()
    {
        var (admin, reporterId) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var neighbour = await AddAssetInSameRoomAsync(asset);
        var elsewhere = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);
        var reportId = (await SeedReportsAsync(asset, reporterId, 1, ReportStatus.WorkOrderRaised)).Single();

        // Six open on the asset and six on its neighbour: twelve open in the room, two over the cap.
        var open = new List<int>();
        foreach (var status in new[]
        {
            WorkOrderStatus.Draft, WorkOrderStatus.AwaitingApproval, WorkOrderStatus.Approved,
            WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress, WorkOrderStatus.Approved
        })
        {
            open.AddRange(await SeedWorkOrdersAsync(asset.Id, reportId, status));
            open.AddRange(await SeedWorkOrdersAsync(neighbour, reportId, status));
        }

        // Finished work on the same asset, and live work in another room: neither is an answer.
        var finished = new List<int>();
        foreach (var status in new[] { WorkOrderStatus.Completed, WorkOrderStatus.Rejected, WorkOrderStatus.Cancelled })
        {
            finished.AddRange(await SeedWorkOrdersAsync(asset.Id, reportId, status));
        }
        var otherRoom = await SeedWorkOrdersAsync(elsewhere.Id, reportId, WorkOrderStatus.Approved);

        var body = await CallToolAsync("get_open_work_orders", workflow.Id, asset.Id);

        Assert.True(body.GetProperty("found").GetBoolean());
        var rows = body.GetProperty("result").EnumerateArray().ToList();
        Assert.Equal(IWorkOrderService.MaxToolOpenWorkOrders, rows.Count);

        var returnedIds = rows.Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.All(returnedIds, id => Assert.Contains(id, open));
        Assert.DoesNotContain(returnedIds, id => finished.Contains(id) || otherRoom.Contains(id));

        // The neighbour's orders are there: sharing a visit is what the room scope is for.
        Assert.Contains(rows, r => r.GetProperty("assetId").GetInt32() == neighbour);

        // Newest first, so the cap drops the two oldest rather than this morning's order.
        Assert.Equal(returnedIds.OrderByDescending(i => i), returnedIds);
        Assert.DoesNotContain(open.Min(), returnedIds);

        // Strategy and status by NAME, like every enum on the wire.
        Assert.All(rows, r => Assert.Equal(JsonValueKind.String, r.GetProperty("status").ValueKind));
    }

    [Fact]
    public async Task GetOpenWorkOrders_TellsAnUnknownAssetApartFromARoomWithNothingOpen()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var asset = await CreateAssetAsync(admin);
        var workflow = await StartWorkflowAsync(admin);

        var quiet = await CallToolAsync("get_open_work_orders", workflow.Id, asset.Id);
        Assert.True(quiet.GetProperty("found").GetBoolean());
        Assert.Empty(quiet.GetProperty("result").EnumerateArray());

        var unknown = await CallToolAsync("get_open_work_orders", workflow.Id, 999999);
        Assert.False(unknown.GetProperty("found").GetBoolean());
    }

    /// <summary>
    /// The strategist proposes; C# decides. There is no tool through which an agent could
    /// approve, raise or re-cost a work order — a request for one is an unknown tool.
    /// </summary>
    [Fact]
    public async Task TheAllowListHasNoToolThatActsOnAWorkOrder()
    {
        var (admin, _) = await CreateAuthenticatedClientAsync(Role.Admin);
        var workflow = await StartWorkflowAsync(admin);
        var agentClient = CreateAgentClient();

        foreach (var name in new[] { "approve_work_order", "create_work_order", "set_strategy" })
        {
            var response = await agentClient.PostAsJsonAsync(
                $"/api/internal/tools/{name}",
                new ToolCallRequest(workflow.Id, 1, "strategist"), JsonOptions);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(HttpClient Client, int UserId)> CreateAuthenticatedClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "ToolPass1", "Test User", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>Client for the agent service: no JWT, just the shared secret header.</summary>
    private HttpClient CreateAgentClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", ApiFactory.AgentSharedSecret);
        return client;
    }

    private async Task<JsonElement> CallToolAsync(string toolName, int workflowId, int id)
    {
        var response = await CreateAgentClient().PostAsJsonAsync(
            $"/api/internal/tools/{toolName}",
            new ToolCallRequest(workflowId, id, "diagnostician"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<WorkflowSummaryDto> StartWorkflowAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/workflows",
            new StartWorkflowRequest("Look into a misbehaving projector"), JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<WorkflowSummaryDto>(JsonOptions))!;
    }

    private async Task<AgentStepDto> SingleStepAsync(HttpClient client, int workflowId)
    {
        var detail = await client.GetFromJsonAsync<WorkflowDetailDto>(
            $"/api/workflows/{workflowId}", JsonOptions);

        return Assert.Single(detail!.Steps);
    }

    private async Task<AssetDto> CreateAssetAsync(
        HttpClient adminClient,
        string name = "Projector",
        string? categoryName = null,
        string? roomName = null)
    {
        var buildingResponse = await adminClient.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Main Building", UniqueCode()), JsonOptions);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await adminClient.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomDto(building!.Id, roomName ?? "Lab", UniqueCode(), 1), JsonOptions);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var categoryResponse = await adminClient.PostAsJsonAsync(
            "/api/assetcategories",
            new CreateAssetCategoryDto(categoryName ?? $"Category {UniqueCode()}", 24), JsonOptions);
        var category = await categoryResponse.Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var assetResponse = await adminClient.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto($"AST-{UniqueCode()}", name, category!.Id, room!.Id, "Epson", "EB-X05",
                new DateOnly(2023, 1, 10), null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);

        return (await assetResponse.Content.ReadFromJsonAsync<AssetDto>(JsonOptions))!;
    }

    /// <summary>
    /// Writes history straight to the database: a ServiceRecord is appended when a work
    /// order completes, never posted by a client, so there is no endpoint for it.
    /// </summary>
    private async Task SeedHistoryAsync(int assetId, IEnumerable<DateOnly> visits)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var on in visits)
        {
            db.ServiceRecords.Add(new ServiceRecord
            {
                AssetId = assetId,
                ServicedOn = on,
                TechnicianName = "S. Perera",
                TechnicianNote = "unit running hot, temp fix",
                Outcome = ServiceOutcome.TemporaryFix
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>A second asset in the same room as <paramref name="sibling"/>, written directly.</summary>
    private async Task<int> AddAssetInSameRoomAsync(AssetDto sibling)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = new Asset
        {
            AssetTag = $"AST-{UniqueCode()}",
            Name = "Split AC",
            AssetCategoryId = sibling.AssetCategoryId,
            RoomId = sibling.RoomId,
            InstalledOn = new DateOnly(2023, 1, 10)
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return asset.Id;
    }

    /// <summary>
    /// One work order in a given status, written directly: the endpoints only ever raise a
    /// Draft and walk it forward, and these tests need every status at once.
    /// </summary>
    private async Task<List<int>> SeedWorkOrdersAsync(int assetId, int reportId, WorkOrderStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = new WorkOrder
        {
            ReportId = reportId,
            AssetId = assetId,
            Status = status,
            Strategy = WorkOrderStrategy.SingleJob,
            EstimatedCost = 4500.00m
        };
        db.WorkOrders.Add(order);
        await db.SaveChangesAsync();

        return new List<int> { order.Id };
    }

    /// <summary>
    /// Reports against a specific asset, written directly: CreateReportDto has no AssetId
    /// (a reporter is not expected to know the tag), so the API cannot produce these.
    /// </summary>
    private async Task<List<int>> SeedReportsAsync(
        AssetDto asset,
        int reporterId,
        int count,
        ReportStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reports = Enumerable.Range(0, count)
            .Select(i => new Report
            {
                ReporterId = reporterId,
                RoomId = asset.RoomId,
                AssetId = asset.Id,
                Description = $"Projector cutting out again, report {i}",
                Status = status
            })
            .ToList();

        db.Reports.AddRange(reports);
        await db.SaveChangesAsync();

        return reports.Select(r => r.Id).ToList();
    }
}
