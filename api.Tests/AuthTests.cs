using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace api.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // The API serialises enums by name, so the tests must too — otherwise these tests
    // would pass against a contract the real clients cannot actually use.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Each test registers its own account so tests never depend on each other's data.
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static async Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string email,
        string password,
        Role role)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, password, "Test User", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    [Fact]
    public async Task Login_WithCorrectPassword_Returns200AndAToken()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email, "CorrectHorse1", Role.Technician);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "CorrectHorse1"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal(email, body.Email);
        Assert.Equal(Role.Technician, body.Role);

        // A JWT is three dot-separated segments; this is a token, not an empty string.
        Assert.Equal(3, body.Token.Split('.').Length);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email, "CorrectHorse1", Role.Reporter);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, "WrongPassword9"), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagerOnly_WithReporterToken_Returns403NotUnauthorized()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, UniqueEmail(), "ReporterPass1", Role.Reporter);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.GetAsync("/api/auth/manager-only");

        // 403, not 401: the server knows exactly who this is, it just will not let them in.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManagerOnly_WithNoToken_Returns401NotForbidden()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/manager-only");

        // The contrast with the test above is the point: 401 means "who are you?",
        // 403 means "I know who you are, and no".
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagerOnly_WithFacilitiesManagerToken_Returns200()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterAsync(client, UniqueEmail(), "ManagerPass1", Role.FacilitiesManager);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.GetAsync("/api/auth/manager-only");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409NotA500()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email, "FirstPass12", Role.Reporter);

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, "SecondPass12", "Someone Else", Role.Technician), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsTheCallerFromTheToken()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        var auth = await RegisterAsync(client, email, "MePass12345", Role.Admin);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(email, body!.Email);
        Assert.Equal(Role.Admin, body.Role);
        Assert.Equal(auth.UserId, body.Id);
    }
}
