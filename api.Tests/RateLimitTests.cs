using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace api.Tests;

/// <summary>An ApiFactory with small budgets put back — ApiFactory itself switches them off.</summary>
public class RateLimitedApiFactory(int authAttemptsPerMinute, int reportsPerHour) : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<RateLimitSettings>();
            services.AddSingleton(new RateLimitSettings
            {
                AuthAttemptsPerMinute = authAttemptsPerMinute,
                ReportsPerHour = reportsPerHour
            });
        });
    }
}

/// <summary>
/// The two rate limits (RateLimitSettings, RateLimitRules), through the real pipeline. Every
/// test builds its own factory, because a budget is state that outlives a request. What is
/// pinned:
///
///   * sign-in attempts are budgeted per client ADDRESS — taken from the LAST X-Forwarded-For
///     entry, the one a proxy appends, so a value a client writes in front cannot dodge it —
///     and login and register share the budget;
///   * reports are budgeted per signed-in USER: one reporter's limit is not another's, and a
///     refused report is not written;
///   * a refused request is a 429 with Retry-After and a ProblemDetails saying when to retry,
///     and an anonymous report is still a 401, not a 429.
/// </summary>
public class RateLimitTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    public async Task SignInAttempts_AreBudgetedPerAddress_And429SaysWhenToRetry()
    {
        using var factory = new RateLimitedApiFactory(authAttemptsPerMinute: 3, reportsPerHour: int.MaxValue);
        var attacker = ClientFrom(factory, "203.0.113.10");
        var wrong = new LoginRequest("nobody@campus.test", "WrongPass1");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.PostAsJsonAsync("/api/auth/login", wrong)).StatusCode);
        }

        var refused = await attacker.PostAsJsonAsync("/api/auth/login", wrong);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // When to come back: a whole number of seconds, within the one-minute window.
        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);

        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.Equal(429, problem!.Status);
        Assert.StartsWith("Too many sign-in attempts from this address. Try again in", problem.Detail);

        // Register shares the budget: the address cannot switch endpoints to keep going.
        var register = await attacker.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(UniqueEmail(), "SignUpPass1", "New Reporter"), JsonOptions);
        Assert.Equal(HttpStatusCode.TooManyRequests, register.StatusCode);

        // Another address has its own budget.
        var someoneElse = ClientFrom(factory, "203.0.113.11");
        Assert.Equal(HttpStatusCode.Unauthorized, (await someoneElse.PostAsJsonAsync("/api/auth/login", wrong)).StatusCode);

        // A value the CLIENT puts in front of the proxy's is ignored: the budget follows the
        // last entry, the one the proxy appended, so the spoofed address changes nothing.
        var spoofing = ClientFrom(factory, "198.51.100.7, 203.0.113.10");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await spoofing.PostAsJsonAsync("/api/auth/login", wrong)).StatusCode);
    }

    [Fact]
    public async Task Reports_AreBudgetedPerUser_AndARefusedReportIsNotWritten()
    {
        using var factory = new RateLimitedApiFactory(authAttemptsPerMinute: int.MaxValue, reportsPerHour: 2);
        var roomId = await CreateRoomAsync(factory);
        var reporter = await ReporterAsync(factory);

        for (var report = 1; report <= 2; report++)
        {
            Assert.Equal(HttpStatusCode.Created, (await FileAsync(reporter, roomId)).StatusCode);
        }

        var refused = await FileAsync(reporter, roomId);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 3600);

        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.StartsWith("You have filed the most reports allowed in an hour. Try again in", problem!.Detail);

        // Nothing was written for the refused one: still two reports, so no third agent run.
        var mine = await reporter.GetFromJsonAsync<PagedResult<ReportListItemDto>>("/api/reports", JsonOptions);
        Assert.Equal(2, mine!.TotalCount);

        // Another reporter's budget is untouched.
        var other = await ReporterAsync(factory);
        Assert.Equal(HttpStatusCode.Created, (await FileAsync(other, roomId)).StatusCode);

        // No token is still "who are you?" — authorization answers before any budget is spent.
        Assert.Equal(HttpStatusCode.Unauthorized, (await FileAsync(factory.CreateClient(), roomId)).StatusCode);
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(61, "2 minutes")]
    [InlineData(3600, "60 minutes")]
    public void TheWait_IsWrittenInWholeUnits_RoundedUp(int seconds, string expected)
    {
        Assert.Equal(expected, RateLimitRules.Describe(seconds));
    }

    // ---------------------------------------------------------------------------------

    /// <summary>A client whose requests arrive as if through a proxy reporting this address.</summary>
    private static HttpClient ClientFrom(ApiFactory factory, string forwardedFor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    private static Task<HttpResponseMessage> FileAsync(HttpClient client, int roomId) =>
        client.PostAsJsonAsync("/api/reports", new CreateReportDto("Projector cuts out mid lecture.", roomId), JsonOptions);

    private static async Task<HttpClient> ReporterAsync(ApiFactory factory)
    {
        var response = await factory.RegisterAsync(new RegisterRequest(UniqueEmail(), "ReportPass1", "Test Reporter", Role.Reporter));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static async Task<int> CreateRoomAsync(ApiFactory factory)
    {
        using var admin = await factory.CreateAdminClientAsync();

        var building = await (await admin.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Engineering Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);
        var room = await (await admin.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall A", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        return room!.Id;
    }
}
