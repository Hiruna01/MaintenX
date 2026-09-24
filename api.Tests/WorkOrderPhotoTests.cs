using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace api.Tests;

/// <summary>
/// POST /api/workorders/{id}/photo — the completion photo. The file rules themselves
/// (types, 5 MB, magic bytes, the ignored file name) are ImageUploadRules and
/// SupabaseStorageService, pinned in depth by ReportPhotoTests; what is pinned here is what
/// is particular to a work order:
///
///   * only the ASSIGNED technician, and only while the job is live work;
///   * the URL lands on the order, under its own folder, and COMPLETING WITHOUT A URL KEEPS
///     IT — the phone uploads first and then completes, and a null must not erase it;
///   * storage failure is a 503 that records nothing, leaving the job open to complete.
/// </summary>
public class WorkOrderPhotoTests : IClassFixture<StorageStubApiFactory>
{
    private readonly StorageStubApiFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly byte[] JpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

    private const string Note = "Lamp housing fan replaced, filter cleaned. Ran 40min, no cutout.";

    public WorkOrderPhotoTests(StorageStubApiFactory factory)
    {
        _factory = factory;
        _factory.Storage.Reset();
    }

    [Fact]
    public async Task Upload_ByTheAssignedTechnician_IsRecorded_AndCompletingWithoutAUrlKeepsIt()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var orderId = await AssignedOrderAsync(manager, technicianId);

        var response = await technician.PostAsync($"/api/workorders/{orderId}/photo", PhotoForm(JpegBytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CompletionPhotoDto>(JsonOptions);
        Assert.Matches(
            $"^{Regex.Escape(StorageStubApiFactory.StorageUrl)}/storage/v1/object/public/photos/workorders/{orderId}/[0-9a-f]{{32}}\\.jpg$",
            created!.CompletionPhotoUrl);

        var (_, body, contentType) = Assert.Single(_factory.Storage.Requests);
        Assert.Equal(JpegBytes, body);
        Assert.Equal("image/jpeg", contentType);

        // Then completed exactly as the phone does it: no URL in the body.
        var complete = await technician.PostAsJsonAsync($"/api/workorders/{orderId}/complete",
            new CompleteWorkOrderDto(1_500m, ServiceOutcome.PartReplaced, Note, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        var detail = await technician.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions);
        Assert.Equal(WorkOrderStatus.Completed, detail!.Status);
        Assert.Equal(created.CompletionPhotoUrl, detail.CompletionPhotoUrl);
    }

    [Fact]
    public async Task Upload_ByAnyoneButTheAssignedTechnician_IsRefused_AndStoresNothing()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, assigneeId) = await ClientAsync(Role.Technician);
        var (otherTechnician, _) = await ClientAsync(Role.Technician);
        var (reporter, _) = await ClientAsync(Role.Reporter);
        var orderId = await AssignedOrderAsync(manager, assigneeId);
        var url = $"/api/workorders/{orderId}/photo";

        var anonymous = await _factory.CreateClient().PostAsync(url, PhotoForm(JpegBytes));
        var byOther = await otherTechnician.PostAsync(url, PhotoForm(JpegBytes));
        var byReporter = await reporter.PostAsync(url, PhotoForm(JpegBytes));
        var byManager = await manager.PostAsync(url, PhotoForm(JpegBytes));

        // 401 with no token; the ownership 403 for a technician who is not the assignee; the
        // policy 403 for everyone else — a manager included, since the photo is evidence of
        // work they did not do.
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byReporter.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_ToAnOrderThatIsNotLiveWork_Is409()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var orderId = await AssignedOrderAsync(manager, technicianId);

        var complete = await technician.PostAsJsonAsync($"/api/workorders/{orderId}/complete",
            new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, Note, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        var response = await technician.PostAsync($"/api/workorders/{orderId}/photo", PhotoForm(JpegBytes));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_ToAnUnknownOrder_Is404()
    {
        var (technician, _) = await ClientAsync(Role.Technician);

        var response = await technician.PostAsync("/api/workorders/999999/photo", PhotoForm(JpegBytes));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WhoseBytesAreNotTheClaimedType_Is400AndStoresNothing()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var orderId = await AssignedOrderAsync(manager, technicianId);

        var response = await technician.PostAsync(
            $"/api/workorders/{orderId}/photo", PhotoForm("not an image"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_Is503_AndTheJobCanStillBeCompleted()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (technician, technicianId) = await ClientAsync(Role.Technician);
        var orderId = await AssignedOrderAsync(manager, technicianId);

        _factory.Storage.Respond = () => throw new HttpRequestException("Connection refused");

        var response = await technician.PostAsync($"/api/workorders/{orderId}/photo", PhotoForm(JpegBytes));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var detail = await technician.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions);
        Assert.Null(detail!.CompletionPhotoUrl);
        Assert.Equal(WorkOrderStatus.Approved, detail.Status);

        // "Complete without photo" — the job was never held hostage to the upload.
        var complete = await technician.PostAsJsonAsync($"/api/workorders/{orderId}/complete",
            new CompleteWorkOrderDto(100m, ServiceOutcome.Resolved, Note, null), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static MultipartFormDataContent PhotoForm(byte[] bytes, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent();
        form.Add(file, "photo", "photo.jpg");
        return form;
    }

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "PhotoPass1", "Test User", role),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>
    /// A room, an asset, a report, and an auto-approved order on it assigned to
    /// <paramref name="technicianId"/> — live work, ready to photograph and complete.
    /// </summary>
    private async Task<int> AssignedOrderAsync(HttpClient manager, int technicianId)
    {
        var anonymous = _factory.CreateClient();

        var building = await (await anonymous.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await anonymous.PostAsJsonAsync(
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

        var raised = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(report!.Id, asset!.Id, WorkOrderStrategy.SingleJob, 500m, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var order = await raised.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);

        var assigned = await manager.PutAsJsonAsync(
            $"/api/workorders/{order!.Id}/assign", new AssignTechnicianDto(technicianId), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        return order.Id;
    }
}
