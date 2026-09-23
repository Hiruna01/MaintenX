using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace api.Tests;

/// <summary>
/// ApiFactory with Supabase Storage replaced by <see cref="StubStorageHandler"/> at the HTTP
/// level. The real SupabaseStorageService still runs — building the object path, the
/// headers and the public URL, and turning failures into null — and only the network is
/// fake, so these tests can never reach a real Supabase project.
/// </summary>
public class StorageStubApiFactory : ApiFactory
{
    public const string StorageUrl = "https://storage.test";
    public const string ServiceKey = "test-service-key";
    public const string Bucket = "photos";

    public StubStorageHandler Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<StorageSettings>();
            services.AddSingleton(new StorageSettings { Url = StorageUrl, ServiceKey = ServiceKey, Bucket = Bucket });

            services.AddHttpClient(SupabaseStorageService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Storage);
        });
    }
}

/// <summary>Records every request sent to "Supabase" and answers however the test says.</summary>
public class StubStorageHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, byte[] Body, string? ContentType)> Requests { get; } = new();

    /// <summary>Null means succeed with 200, as Supabase does.</summary>
    public Func<HttpResponseMessage>? Respond { get; set; }

    public void Reset()
    {
        Requests.Clear();
        Respond = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read now: the content is disposed with the request once the service is done.
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        Requests.Add((request, body, request.Content?.Headers.ContentType?.MediaType));

        return Respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"Key":"photos/stub"}""", Encoding.UTF8, "application/json")
        };
    }
}

public class ReportPhotoTests : IClassFixture<StorageStubApiFactory>
{
    private readonly StorageStubApiFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The smallest thing that passes as a PNG: its eight-byte signature and a little more.</summary>
    private static readonly byte[] PngBytes =
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

    private static readonly byte[] JpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

    public ReportPhotoTests(StorageStubApiFactory factory)
    {
        _factory = factory;

        // One handler is shared by every test in the class; xUnit runs them one at a time.
        _factory.Storage.Reset();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<HttpClient> CreateAuthenticatedClientAsync(Role role = Role.Reporter)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(UniqueEmail(), "PhotoPass1", "Test User", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }

    /// <summary>Files a report as the given client and returns its id.</summary>
    private async Task<int> CreateReportAsync(HttpClient reporter)
    {
        var client = _factory.CreateClient();

        var buildingResponse = await client.PostAsJsonAsync(
            "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions);
        var building = await buildingResponse.Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var roomResponse = await client.PostAsJsonAsync(
            "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        var reportResponse = await reporter.PostAsJsonAsync(
            "/api/reports",
            new CreateReportDto("The projector keeps cutting out during lectures.", room!.Id),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, reportResponse.StatusCode);

        var report = await reportResponse.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);
        return report!.Id;
    }

    private static MultipartFormDataContent PhotoForm(
        byte[] bytes, string contentType = "image/png", string fileName = "fault.png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent();
        form.Add(file, "photo", fileName);
        return form;
    }

    private async Task<string?> ReadPhotoUrlAsync(HttpClient client, int reportId)
    {
        var report = await client.GetFromJsonAsync<ReportDetailDto>($"/api/reports/{reportId}", JsonOptions);
        return report!.PhotoUrl;
    }

    [Fact]
    public async Task Upload_ByTheReporter_Returns201AndRecordsTheUrl()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var response = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ReportPhotoDto>(JsonOptions);
        Assert.Matches(
            $"^{Regex.Escape(StorageStubApiFactory.StorageUrl)}/storage/v1/object/public/photos/reports/{reportId}/[0-9a-f]{{32}}\\.png$",
            created!.PhotoUrl);

        // CreatedAtAction points at the report, and the report now carries the same URL.
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(created.PhotoUrl, await ReadPhotoUrlAsync(reporter, reportId));

        // What reached "Supabase": the bytes as sent, typed, and authenticated with the
        // service key — which stays on the server and appears nowhere in the response.
        var (request, body, contentType) = Assert.Single(_factory.Storage.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(PngBytes, body);
        Assert.Equal("image/png", contentType);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(StorageStubApiFactory.ServiceKey, request.Headers.Authorization.Parameter);
        Assert.DoesNotContain(StorageStubApiFactory.ServiceKey, created.PhotoUrl);
    }

    [Fact]
    public async Task Upload_IgnoresTheUploadedFileName()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        // The classic path-traversal payload, and a mismatched extension for good measure.
        // Neither may reach the storage path: the object name is a server-generated GUID and
        // its extension comes from the validated content type.
        var response = await reporter.PostAsync(
            $"/api/reports/{reportId}/photo",
            PhotoForm(JpegBytes, "image/jpeg", "../../../other-bucket/evil.png"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var (request, _, _) = Assert.Single(_factory.Storage.Requests);
        var path = request.RequestUri!.AbsolutePath;

        Assert.Matches($"^/storage/v1/object/photos/reports/{reportId}/[0-9a-f]{{32}}\\.jpg$", path);
        Assert.DoesNotContain("evil", request.RequestUri.OriginalString);
        Assert.DoesNotContain("..", request.RequestUri.OriginalString);
    }

    [Fact]
    public async Task Upload_OfExactlyFiveMegabytes_IsAccepted()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var bytes = new byte[ImageUploadRules.MaxBytes];
        PngBytes.CopyTo(bytes, 0);

        var response = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(bytes));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OverFiveMegabytes_Returns400AndUploadsNothing()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var bytes = new byte[ImageUploadRules.MaxBytes + 1];
        PngBytes.CopyTo(bytes, 0);

        var response = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(bytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
        Assert.Null(await ReadPhotoUrlAsync(reporter, reportId));
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    public async Task Upload_OfAnotherContentType_Returns400AndUploadsNothing(string contentType)
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var response = await reporter.PostAsync(
            $"/api/reports/{reportId}/photo", PhotoForm(PngBytes, contentType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_WhoseBytesAreNotTheClaimedType_Returns400AndUploadsNothing()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        // Labelled image/png, and it is not one. The content type is the client's claim.
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        var response = await reporter.PostAsync(
            $"/api/reports/{reportId}/photo", PhotoForm(html, "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_ByAnotherUser_Returns403AndUploadsNothing()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var stranger = await CreateAuthenticatedClientAsync();

        var response = await stranger.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_ByAManagerOnSomeoneElsesReport_Returns403()
    {
        // The photo is part of the reporter's account of the fault; seniority does not
        // make it anyone else's to attach.
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var manager = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);

        var response = await manager.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ToAnUnknownReport_Returns404()
    {
        var reporter = await CreateAuthenticatedClientAsync();

        var response = await reporter.PostAsync("/api/reports/999999/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_factory.Storage.Requests);
    }

    [Fact]
    public async Task Upload_WithNoToken_Returns401()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var response = await _factory.CreateClient()
            .PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithNoFilePart_Returns400()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var form = new MultipartFormDataContent { { new StringContent("not a file"), "caption" } };

        var response = await reporter.PostAsync($"/api/reports/{reportId}/photo", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("refused")]
    [InlineData("timeout")]
    public async Task Upload_WhenStorageFails_Returns503AndRecordsNoUrl(string failure)
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        _factory.Storage.Respond = failure switch
        {
            "unreachable" => () => throw new HttpRequestException("Connection refused"),
            "refused" => () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("storage is having a bad day")
            },
            // What HttpClient throws when its own Timeout elapses.
            _ => () => throw new TaskCanceledException("The request timed out.")
        };

        var response = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // A URL pointing at nothing is worse than no photo.
        Assert.Null(await ReadPhotoUrlAsync(reporter, reportId));
    }

    [Fact]
    public async Task Upload_WhenStorageFails_KeepsTheEarlierPhoto()
    {
        var reporter = await CreateAuthenticatedClientAsync();
        var reportId = await CreateReportAsync(reporter);

        var first = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));
        var firstUrl = (await first.Content.ReadFromJsonAsync<ReportPhotoDto>(JsonOptions))!.PhotoUrl;

        _factory.Storage.Respond = () => throw new HttpRequestException("Connection refused");

        var second = await reporter.PostAsync($"/api/reports/{reportId}/photo", PhotoForm(PngBytes));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(firstUrl, await ReadPhotoUrlAsync(reporter, reportId));
    }
}
