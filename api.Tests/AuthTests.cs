using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Registers an account of any role — as the factory's bootstrap Admin, because only an
    /// Admin may create a role other than Reporter. The registration rules themselves are
    /// pinned by the Register_* tests below, which call the endpoint directly.
    /// </summary>
    private async Task<AuthResponse> RegisterAsync(
        HttpClient client,
        string email,
        string password,
        Role role)
    {
        var response = await _factory.RegisterAsync(new RegisterRequest(email, password, "Test User", role));

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

        var first = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "FirstPass12", "Someone"), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "SecondPass12", "Someone Else"), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------
    // Who may create which account. Anyone may sign up as a Reporter — that is the phone's
    // registration screen. Every other role is an Admin's to hand out: the role in the body is
    // what is ASKED for, the caller's token decides whether it is granted.
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task Register_Anonymously_WithNoRole_CreatesAReporterAndSignsThemIn()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "SignUpPass1", "New Reporter"), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.Equal(Role.Reporter, body!.Role);
        Assert.Equal(3, body.Token.Split('.').Length);
    }

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.FacilitiesManager)]
    [InlineData(Role.Technician)]
    public async Task Register_Anonymously_AskingForAnyOtherRole_Is403_AndCreatesNothing(Role role)
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, "Escalate123", "Not An Admin", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Nothing was written: the account does not exist to sign in to.
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Escalate123"), JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    [InlineData(Role.FacilitiesManager)]
    public async Task Register_AsANonAdmin_AskingForAdmin_Is403(Role callerRole)
    {
        var caller = _factory.CreateClient();
        var auth = await RegisterAsync(caller, UniqueEmail(), "CallerPass1", callerRole);
        caller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await caller.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(UniqueEmail(), "Escalate123", "Friend", Role.Admin), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    [InlineData(Role.FacilitiesManager)]
    [InlineData(Role.Admin)]
    public async Task Register_AsAnAdmin_CanCreateEveryRole(Role role)
    {
        using var admin = await _factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(UniqueEmail(), "StaffPass12", "Staff Member", role), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(role, (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!.Role);
    }

    // -----------------------------------------------------------------------------------
    // The fallback policy: an endpoint that says nothing about authorization needs a
    // signed-in user, so forgetting [Authorize] fails closed. The endpoints that are public on
    // purpose are listed here exactly — a new one has to be added to this list deliberately.
    // -----------------------------------------------------------------------------------

    [Fact]
    public void TheOnlyAnonymousEndpoints_AreLoginRegisterHealthAndTheAgentToolRouter()
    {
        var anonymous = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!.TrimStart('/').ToLowerInvariant())
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { "api/auth/login", "api/auth/register", "api/internal/tools/{toolname}", "health" },
            anonymous);
    }

    [Fact]
    public void AnEndpointThatSaysNothingAboutAuthorization_NeedsASignedInUser()
    {
        var fallback = _factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Health_NeedsNoToken_ButTheRoomList_Does()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rooms")).StatusCode);
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
