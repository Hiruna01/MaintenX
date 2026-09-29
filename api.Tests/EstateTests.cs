using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace api.Tests;

/// <summary>
/// Buildings and rooms: reads for every signed-in user, writes for an Admin — the asset
/// registry's split — and every refusal a status code rather than a constraint violation out
/// of the driver. These controllers once had no [Authorize] at all; the fallback policy and
/// the explicit attributes are both pinned here.
/// </summary>
public class EstateTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ApiFactory _factory;

    public EstateTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    public async Task WithoutAToken_EveryReadAndWriteIs401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/buildings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rooms")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("B", UniqueCode()), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/rooms/1")).StatusCode);
    }

    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    [InlineData(Role.FacilitiesManager)]
    public async Task ANonAdmin_ReadsTheEstate_ButCannotChangeIt(Role role)
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (buildingId, roomId) = await NewRoomAsync(admin);
        var client = await ClientAsync(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/buildings/{buildingId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("B", UniqueCode()), JsonOptions)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/rooms/{roomId}", new CreateRoomDto(buildingId, "Renamed", UniqueCode(), 1), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/rooms/{roomId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/buildings/{buildingId}")).StatusCode);
    }

    [Fact]
    public async Task ABuildingCodeAlreadyInUse_Is409NotA500_OnCreateAndOnRename()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var code = UniqueCode();

        var first = await admin.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("First", code), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/buildings", new CreateBuildingDto("Second", code), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var other = await (await admin.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Other", UniqueCode()), JsonOptions)).Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);
        var renamed = await admin.PutAsJsonAsync($"/api/buildings/{other!.Id}", new CreateBuildingDto("Other", code), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        // Keeping its own code is not a clash with itself.
        var unchanged = await admin.PutAsJsonAsync($"/api/buildings/{other.Id}", new CreateBuildingDto("Other, renamed", other.Code), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
    }

    [Fact]
    public async Task ABuildingWithRooms_Is409ToDelete_AndAnEmptyOneGoes()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (buildingId, roomId) = await NewRoomAsync(admin);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/buildings/{buildingId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/rooms/{roomId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/buildings/{buildingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/buildings/{buildingId}")).StatusCode);
    }

    [Fact]
    public async Task ARoomAnAssetStandsIn_Is409ToDelete_AndTheAssetIsUntouched()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, roomId) = await NewRoomAsync(admin);

        var category = await (await admin.PostAsJsonAsync(
            "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);
        var asset = await admin.PostAsJsonAsync(
            "/api/assets",
            new CreateAssetDto(UniqueCode(), "Projector", category!.Id, roomId, "Epson", "EB-990U",
                new DateOnly(2024, 1, 10), null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, asset.StatusCode);
        var assetId = (await asset.Content.ReadFromJsonAsync<AssetDto>(JsonOptions))!.Id;

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/rooms/{roomId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/assets/{assetId}")).StatusCode);
    }

    [Fact]
    public async Task MovingARoomToABuildingThatDoesNotExist_Is400_NotA404()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, roomId) = await NewRoomAsync(admin);

        var response = await admin.PutAsJsonAsync(
            $"/api/rooms/{roomId}", new CreateRoomDto(999_999, "Lecture Hall", UniqueCode(), 1), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(int BuildingId, int RoomId)> NewRoomAsync(HttpClient admin)
    {
        var building = await (await admin.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);
        var room = await (await admin.PostAsJsonAsync(
            "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        return (building.Id, room!.Id);
    }

    private async Task<HttpClient> ClientAsync(Role role)
    {
        var response = await _factory.RegisterAsync(
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "EstatePass1", "Test User", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!.Token);
        return client;
    }
}
