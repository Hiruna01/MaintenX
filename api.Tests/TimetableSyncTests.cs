using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace api.Tests;

/// <summary>
/// ApiFactory with Google Calendar replaced by <see cref="StubCalendarClient"/> and the clock
/// by <see cref="MutableClock"/>. The real GoogleCalendarSyncService runs — the timeout, the
/// failure classification, the room mapping and the upsert — and only Google is fake, so no
/// test can reach a real calendar.
/// </summary>
public class TimetableStubApiFactory : ApiFactory
{
    /// <summary>
    /// Short, so the timeout test takes a fraction of a second rather than the real ten.
    /// What is under test is that the limit is enforced, not its value.
    /// </summary>
    public const double TimeoutSeconds = 0.3;

    /// <summary>Monday 5 October 2026, 07:30 in Colombo — before the working day starts.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);

    public StubCalendarClient Calendar { get; } = new();

    public MutableClock Clock { get; } = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<GoogleCalendarSettings>();
            services.AddSingleton(new GoogleCalendarSettings
            {
                CalendarId = "campus-timetable@group.calendar.google.com",
                // Never parsed: the client that would parse it is replaced below.
                ServiceAccountJson = "{}",
                TimeoutSeconds = TimeoutSeconds
            });

            services.RemoveAll<IGoogleCalendarClient>();
            services.AddSingleton<IGoogleCalendarClient>(Calendar);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>A clock the test moves by hand, so "25 hours later" takes no time at all.</summary>
public sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Stands in for Google: returns <see cref="Events"/>, or whatever <see cref="Respond"/> says.</summary>
public class StubCalendarClient : IGoogleCalendarClient
{
    public List<Event> Events { get; } = new();

    /// <summary>Null means answer with <see cref="Events"/>.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<Event>>>? Respond { get; set; }

    public int Calls { get; private set; }

    public (DateTime From, DateTime To)? LastWindow { get; private set; }

    public void Reset()
    {
        Events.Clear();
        Respond = null;
        Calls = 0;
        LastWindow = null;
    }

    public Task<IReadOnlyList<Event>> ListEventsAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastWindow = (from, to);
        return Respond?.Invoke(cancellationToken)
            ?? Task.FromResult<IReadOnlyList<Event>>(Events.ToList());
    }
}

/// <summary>
/// POST /api/timetable/sync and the ClassScheduleSlot cache behind it.
///
/// Two halves. The sync itself: events land in the right room by their location's room code,
/// a re-sync updates rather than duplicates, and a row is only removed when Google says so.
/// And the failure handling, which is the half that matters most: whatever goes wrong at
/// Google — a timeout, a 5xx, a refused credential — the answer is a 200 that says it is
/// degraded and how old the cache is, the cached classes stay exactly where they were, and
/// the slot finder keeps working from them without ever calling Google.
/// </summary>
public class TimetableSyncTests : IClassFixture<TimetableStubApiFactory>
{
    private readonly TimetableStubApiFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Sri Lanka's offset, which is how Google returns a Colombo calendar's times.</summary>
    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);

    /// <summary>Tuesday 6 October 2026, 09:00–11:00 in Colombo: 03:30–05:30 UTC.</summary>
    private static readonly DateTimeOffset LectureStart = new(2026, 10, 6, 9, 0, 0, Colombo);
    private static readonly DateTimeOffset LectureEnd = new(2026, 10, 6, 11, 0, 0, Colombo);

    public TimetableSyncTests(TimetableStubApiFactory factory)
    {
        _factory = factory;

        // One stub, one clock and one database for the whole class; xUnit runs its tests
        // one at a time. The cache age is read across the whole table, so each test starts
        // from an empty one.
        _factory.Calendar.Reset();
        _factory.Clock.Now = TimetableStubApiFactory.Start;

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().ClassScheduleSlots.ExecuteDelete();
    }

    // ---------------------------------------------------------------------------------------
    // Who may sync
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sync_Returns401_WithoutAToken()
    {
        var response = await _factory.CreateClient().PostAsync("/api/timetable/sync", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.Calendar.Calls);
    }

    /// <summary>An Admin too: the policy names FacilitiesManager and Role has no seniority.</summary>
    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    [InlineData(Role.Admin)]
    public async Task Sync_Returns403_ForAnyoneButAFacilitiesManager(Role role)
    {
        var client = await CreateAuthenticatedClientAsync(role);

        var response = await client.PostAsync("/api/timetable/sync", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _factory.Calendar.Calls);
    }

    // ---------------------------------------------------------------------------------------
    // The sync
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Sync_PlacesEachEventInTheRoomItsLocationNames_InUtc()
    {
        var code = UniqueCode();
        var roomId = await AddRoomAsync(code);

        // Lower case and padded: a location is typed by a person into Google Calendar.
        _factory.Calendar.Events.Add(Lecture("evt-place", $"  {code.ToLowerInvariant()} ", "SE3090 Lecture"));

        var result = await SyncAsync();

        Assert.False(result.Degraded);
        Assert.Null(result.FailureReason);
        Assert.Equal(1, result.SyncedCount);
        Assert.Equal(0, result.SkippedCount);

        var row = await SingleClassAsync("evt-place");
        Assert.Equal(roomId, row.RoomId);
        Assert.Equal("SE3090 Lecture", row.Title);
        Assert.Equal(new DateTime(2026, 10, 6, 3, 30, 0), row.StartsAt);
        Assert.Equal(new DateTime(2026, 10, 6, 5, 30, 0), row.EndsAt);
        Assert.Equal(_factory.Clock.Now.UtcDateTime, row.SyncedAt);

        // Just synced: fresh, with an age of zero rather than null.
        Assert.Equal(0, result.CacheAgeMinutes);
        Assert.False(result.IsStale);
        Assert.Null(result.StalenessWarning);

        // From now, about a semester ahead.
        var window = _factory.Calendar.LastWindow!.Value;
        Assert.Equal(_factory.Clock.Now.UtcDateTime, window.From);
        Assert.Equal(_factory.Clock.Now.UtcDateTime.AddDays(GoogleCalendarSettings.SyncDaysAhead), window.To);
    }

    /// <summary>
    /// THE UPSERT. The same event pulled twice is one row, carrying the new times and title —
    /// a second row would read as a second lecture in the room at the same time.
    /// </summary>
    [Fact]
    public async Task Resync_UpdatesTheRow_RatherThanAddingASecondOne()
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture("evt-upsert", code, "SE3090 Lecture"));
        await SyncAsync();

        // Rescheduled an hour later and retitled, then synced an hour on.
        _factory.Calendar.Events.Clear();
        _factory.Calendar.Events.Add(Lecture("evt-upsert", code, "SE3090 Lecture (moved)", shiftHours: 1));
        _factory.Clock.Now = _factory.Clock.Now.AddHours(1);

        var result = await SyncAsync();

        Assert.Equal(1, result.SyncedCount);

        using var scope = _factory.Services.CreateScope();
        var rows = await Db(scope).ClassScheduleSlots.Where(c => c.ExternalEventId == "evt-upsert").ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("SE3090 Lecture (moved)", row.Title);
        Assert.Equal(new DateTime(2026, 10, 6, 4, 30, 0), row.StartsAt);
        Assert.Equal(_factory.Clock.Now.UtcDateTime, row.SyncedAt);
    }

    /// <summary>
    /// SyncedAt and UpdatedAt are different facts: when a class was last CONFIRMED, and when
    /// it last CHANGED. A re-sync that finds nothing different moves the first only.
    /// </summary>
    [Fact]
    public async Task Resync_OfAnUnchangedClass_MovesSyncedAt_ButNotUpdatedAt()
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture("evt-unchanged", code, "SE3090 Lecture"));
        await SyncAsync();
        var before = await SingleClassAsync("evt-unchanged");

        // UpdatedAt is stamped from the real clock, so give it time to move if it is going to.
        await Task.Delay(20);
        _factory.Clock.Now = _factory.Clock.Now.AddHours(1);
        await SyncAsync();

        var after = await SingleClassAsync("evt-unchanged");
        Assert.Equal(_factory.Clock.Now.UtcDateTime, after.SyncedAt);
        Assert.NotEqual(before.SyncedAt, after.SyncedAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    /// <summary>
    /// Nothing is guessed. A location naming no room, a code two rooms share, no location at
    /// all, and an all-day event (a holiday banner, not a class) are all skipped.
    /// </summary>
    [Fact]
    public async Task Sync_SkipsEventsItCannotPlaceInExactlyOneRoom()
    {
        var shared = UniqueCode();
        await AddRoomAsync(shared);
        await AddRoomAsync(shared);
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.AddRange(new[]
        {
            Lecture("evt-nowhere", "Main Auditorium", "Guest talk"),
            Lecture("evt-shared", shared, "Ambiguous"),
            Lecture("evt-no-location", null, "Somewhere"),
            new Event
            {
                Id = "evt-all-day",
                Summary = "Poya holiday",
                Location = code,
                Start = new EventDateTime { Date = "2026-10-06" },
                End = new EventDateTime { Date = "2026-10-07" }
            }
        });

        var result = await SyncAsync();

        Assert.False(result.Degraded);
        Assert.Equal(0, result.SyncedCount);
        Assert.Equal(4, result.SkippedCount);

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(0, await Db(scope).ClassScheduleSlots.CountAsync());
    }

    [Fact]
    public async Task Sync_RemovesAClass_WhenGoogleReportsItCancelled()
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture("evt-cancel", code, "SE3090 Lecture"));
        await SyncAsync();

        _factory.Calendar.Events[0].Status = "cancelled";
        var result = await SyncAsync();

        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Null(await FindClassAsync("evt-cancel"));
    }

    /// <summary>Moved to a location that is not one of our rooms: not in the room we had it in.</summary>
    [Fact]
    public async Task Sync_RemovesAClass_WhenGoogleMovesItOutOfEveryKnownRoom()
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture("evt-moved-out", code, "SE3090 Lecture"));
        await SyncAsync();

        _factory.Calendar.Events[0].Location = "Online (Zoom)";
        var result = await SyncAsync();

        Assert.Equal(1, result.RemovedCount);
        Assert.Null(await FindClassAsync("evt-moved-out"));
    }

    /// <summary>
    /// ABSENCE IS NOT EVIDENCE. An event missing from a reply keeps its row: a missing class
    /// makes its room look free, which is the direction that books maintenance into a
    /// lecture. So an empty reply — a mistyped calendar id, say — cannot wipe the timetable.
    /// </summary>
    [Fact]
    public async Task Sync_KeepsAClass_ThatIsMerelyMissingFromTheReply()
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture("evt-missing", code, "SE3090 Lecture"));
        await SyncAsync();

        _factory.Calendar.Events.Clear();
        var result = await SyncAsync();

        Assert.False(result.Degraded);
        Assert.Equal(0, result.RemovedCount);
        Assert.NotNull(await FindClassAsync("evt-missing"));
    }

    // ---------------------------------------------------------------------------------------
    // Failure handling
    // ---------------------------------------------------------------------------------------

    public static TheoryData<string, TimetableSyncFailure> GoogleFailures => new()
    {
        { "503", TimetableSyncFailure.GoogleServerError },
        { "500", TimetableSyncFailure.GoogleServerError },
        { "token-refused", TimetableSyncFailure.AuthenticationFailed },
        { "401", TimetableSyncFailure.AuthenticationFailed },
        { "403", TimetableSyncFailure.AuthenticationFailed },
        { "404", TimetableSyncFailure.Rejected },
        { "unreachable", TimetableSyncFailure.Unreachable }
    };

    /// <summary>What each named failure looks like coming out of the Google client library.</summary>
    private static Exception GoogleFailure(string name) => name switch
    {
        "token-refused" => new TokenResponseException(
            new TokenErrorResponse { Error = "invalid_grant", ErrorDescription = "Invalid JWT Signature." },
            HttpStatusCode.BadRequest),
        "unreachable" => new HttpRequestException("No such host is known. (oauth2.googleapis.com:443)"),
        _ => new GoogleApiException("calendar", $"Google answered {name}.")
        {
            HttpStatusCode = (HttpStatusCode)int.Parse(name)
        }
    };

    /// <summary>
    /// A 200 that says it is degraded and why, the cached class exactly as it was, and a
    /// warning in the log. Never a 500 — Google being down is not this API being broken.
    /// </summary>
    [Theory]
    [MemberData(nameof(GoogleFailures))]
    public async Task Sync_WhenGoogleFails_Returns200Degraded_AndLeavesTheCacheInPlace(
        string failure, TimetableSyncFailure expected)
    {
        var cached = await CacheAClassAsync("evt-kept");
        var warningsBefore = SyncWarnings();

        _factory.Clock.Now = _factory.Clock.Now.AddHours(3);
        _factory.Calendar.Respond = _ => throw GoogleFailure(failure);

        var result = await SyncAsync();

        Assert.True(result.Degraded);
        Assert.Equal(expected, result.FailureReason);
        Assert.Equal(0, result.SyncedCount);

        // The age of the cache, which is what a degraded answer is for.
        Assert.Equal(cached.SyncedAt, result.LastSyncedAt);
        Assert.Equal(180, result.CacheAgeMinutes);
        Assert.False(result.IsStale);

        var after = await SingleClassAsync("evt-kept");
        Assert.Equal(cached.SyncedAt, after.SyncedAt);
        Assert.Equal(cached.StartsAt, after.StartsAt);
        Assert.Equal(cached.UpdatedAt, after.UpdatedAt);

        Assert.True(SyncWarnings() > warningsBefore);
    }

    /// <summary>
    /// THE EXPLICIT TIMEOUT. A Google that never answers is given up on after
    /// Google:TimeoutSeconds, and the request comes back degraded instead of hanging.
    /// </summary>
    [Fact]
    public async Task Sync_WhenGoogleNeverAnswers_GivesUpAtTheTimeout_AndReturnsDegraded()
    {
        await CacheAClassAsync("evt-timeout");
        _factory.Calendar.Respond = async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await (await CreateAuthenticatedClientAsync(Role.FacilitiesManager))
            .PostAsync("/api/timetable/sync", null);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // By NAME on the wire, like every enum in this API.
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"failureReason\":\"Timeout\"", json);
        Assert.Contains("\"degraded\":true", json);

        // Well past the 0.3s limit, well short of hanging.
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(TimetableStubApiFactory.TimeoutSeconds), TimeSpan.FromSeconds(10));
        Assert.NotNull(await FindClassAsync("evt-timeout"));
    }

    /// <summary>More than 24 hours is stale; exactly 24 is not.</summary>
    [Theory]
    [InlineData(24 * 60, false)]
    [InlineData(24 * 60 + 1, true)]
    public async Task Sync_WhenDegraded_WarnsOnlyOnceTheCacheIsOlderThan24Hours(int ageMinutes, bool stale)
    {
        await CacheAClassAsync("evt-age");
        _factory.Clock.Now = _factory.Clock.Now.AddMinutes(ageMinutes);
        _factory.Calendar.Respond = _ => throw GoogleFailure("503");

        var result = await SyncAsync();

        Assert.True(result.Degraded);
        Assert.Equal(ageMinutes, result.CacheAgeMinutes);
        Assert.Equal(stale, result.IsStale);
        Assert.Equal(stale, result.StalenessWarning is not null);
    }

    /// <summary>Null is not zero: an empty cache has no age, and it is not fresh either.</summary>
    [Fact]
    public async Task Sync_WhenDegradedWithNothingCached_SaysSo_WithANullAge()
    {
        _factory.Calendar.Respond = _ => throw GoogleFailure("503");

        var result = await SyncAsync();

        Assert.True(result.Degraded);
        Assert.Null(result.LastSyncedAt);
        Assert.Null(result.CacheAgeMinutes);
        Assert.True(result.IsStale);
        Assert.Contains("ever been synced", result.StalenessWarning);
    }

    /// <summary>
    /// THE SLOT FINDER READS THE CACHE, NEVER GOOGLE. With Google down, it still keeps the
    /// cached lecture (and its buffer) out of every slot it offers — and it does not so much
    /// as call the calendar client to do it.
    /// </summary>
    [Fact]
    public async Task SlotFinder_KeepsWorkingFromTheCache_WhileGoogleIsDown()
    {
        var code = UniqueCode();
        var roomId = await AddRoomAsync(code);
        var assetId = await AddAssetAsync(roomId);

        _factory.Calendar.Events.Add(Lecture("evt-slots", code, "SE3090 Lecture"));
        await SyncAsync();

        _factory.Calendar.Respond = _ => throw GoogleFailure("503");
        Assert.True((await SyncAsync()).Degraded);
        var callsBefore = _factory.Calendar.Calls;

        var manager = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);
        var response = await manager.GetAsync(
            $"/api/workorders/slots/available?assetId={assetId}&durationMinutes=60"
            + "&fromDate=2026-10-06&toDate=2026-10-06");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slots = (await response.Content.ReadFromJsonAsync<List<AvailableSlotDto>>(JsonOptions))!;

        Assert.NotEmpty(slots);
        Assert.Equal(callsBefore, _factory.Calendar.Calls);

        var buffer = TimeSpan.FromMinutes(SchedulingSettings.DefaultClassBufferMinutes);
        var blockedFrom = LectureStart.UtcDateTime - buffer;
        var blockedTo = LectureEnd.UtcDateTime + buffer;
        Assert.All(slots, slot =>
            Assert.False(slot.StartsAt.ToUniversalTime() < blockedTo && slot.EndsAt.ToUniversalTime() > blockedFrom,
                $"{slot.StartsAt:o}–{slot.EndsAt:o} overlaps the cached lecture."));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static Event Lecture(string id, string? location, string title, int shiftHours = 0) => new()
    {
        Id = id,
        Summary = title,
        Location = location,
        Status = "confirmed",
        Start = new EventDateTime { DateTimeDateTimeOffset = LectureStart.AddHours(shiftHours) },
        End = new EventDateTime { DateTimeDateTimeOffset = LectureEnd.AddHours(shiftHours) }
    };

    /// <summary>Syncs one lecture into a fresh room and returns its row, for the failure tests to protect.</summary>
    private async Task<ClassScheduleSlot> CacheAClassAsync(string eventId)
    {
        var code = UniqueCode();
        await AddRoomAsync(code);

        _factory.Calendar.Events.Add(Lecture(eventId, code, "SE3090 Lecture"));
        Assert.False((await SyncAsync()).Degraded);

        return await SingleClassAsync(eventId);
    }

    private async Task<TimetableSyncResultDto> SyncAsync()
    {
        var manager = await CreateAuthenticatedClientAsync(Role.FacilitiesManager);
        var response = await manager.PostAsync("/api/timetable/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TimetableSyncResultDto>(JsonOptions))!;
    }

    private int SyncWarnings() => _factory.Logs.Entries.Count(e =>
        e.Level == LogLevel.Warning && e.Category == typeof(GoogleCalendarSyncService).FullName);

    private static AppDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<ClassScheduleSlot?> FindClassAsync(string eventId)
    {
        using var scope = _factory.Services.CreateScope();
        return await Db(scope).ClassScheduleSlots.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ExternalEventId == eventId);
    }

    private async Task<ClassScheduleSlot> SingleClassAsync(string eventId)
    {
        var row = await FindClassAsync(eventId);
        Assert.NotNull(row);

        // SQLite hands DateTimes back with no Kind; compare as the UTC they are.
        row.SyncedAt = DateTime.SpecifyKind(row.SyncedAt, DateTimeKind.Utc);
        return row;
    }

    private static string UniqueCode() => $"R-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private async Task<int> AddRoomAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);

        var building = new Building { Name = "Main Academic Block", Code = UniqueCode() };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();

        var room = new Room { BuildingId = building.Id, Name = "Lecture Hall", Code = code, Floor = 1 };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        return room.Id;
    }

    private async Task<int> AddAssetAsync(int roomId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);

        var category = new AssetCategory { Name = $"Projector {Guid.NewGuid():N}", DefaultWarrantyMonths = 24 };
        db.AssetCategories.Add(category);
        await db.SaveChangesAsync();

        var asset = new Asset
        {
            AssetTag = UniqueCode(),
            Name = "Lecture Hall Projector",
            AssetCategoryId = category.Id,
            RoomId = roomId,
            InstalledOn = new DateOnly(2025, 1, 10)
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        return asset.Id;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SyncPass1", "Test User", role),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return client;
    }
}

/// <summary>
/// The plain ApiFactory configures no Google Calendar, which is exactly the state of a
/// teammate's machine that has never set one up: the API boots, and a sync is a degraded 200
/// that never tries to call Google.
/// </summary>
public class TimetableSyncNotConfiguredTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TimetableSyncNotConfiguredTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sync_WithNoGoogleConfiguration_Returns200Degraded_NotConfigured()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SyncPass1", "Manager", Role.FacilitiesManager),
            options);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var response = await client.PostAsync("/api/timetable/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TimetableSyncResultDto>(options);
        Assert.True(result!.Degraded);
        Assert.Equal(TimetableSyncFailure.NotConfigured, result.FailureReason);
    }
}
