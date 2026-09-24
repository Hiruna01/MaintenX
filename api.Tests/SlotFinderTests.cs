using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

/// <summary>
/// The slot finder's boundaries, through GET slots/available and POST {id}/schedule against
/// a real database. SlotRulesTests pins the same boundaries as pure functions; these pin that
/// they SURVIVE THE WIRING — the room read off the asset, the classes loaded for the right
/// days, campus-local dates, and the booking re-running the same Check as the offer.
///
/// The boundary cases put a lecture where its 15-minute buffer lands exactly on the
/// 30-minute grid: 10:15-10:45 blocks 10:00-11:00, so a visit ENDING at 10:00 or STARTING at
/// 11:00 merely touches it and is free, and anything reaching a minute inside is not.
/// </summary>
public class SlotFinderTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SlotFinderTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task ASlotTouchingTheClassBuffer_IsOffered_AndOneInsideItIsNot()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var assetId = await NewAssetAsync();
        var day = FutureMonday();

        await AddClassAsync(await RoomOfAsync(assetId), Local(day, 10, 15), Local(day, 10, 45));

        var starts = (await AvailableAsync(manager, assetId, day, day, 60)).Select(s => s.StartsAt).ToList();

        // 09:00-10:00 ends exactly where the buffer starts; 11:00-12:00 starts exactly where
        // it ends. Touching is not overlapping.
        Assert.Contains(Local(day, 9), starts);
        Assert.Contains(Local(day, 11), starts);

        // 09:30, 10:00 and 10:30 each reach into 10:00-11:00.
        Assert.DoesNotContain(Local(day, 9, 30), starts);
        Assert.DoesNotContain(Local(day, 10), starts);
        Assert.DoesNotContain(Local(day, 10, 30), starts);
    }

    [Fact]
    public async Task TheBuffer_IsWhatBlocksTheSlot_NotTheClassItself()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var assetId = await NewAssetAsync();
        var day = FutureMonday();

        // A lecture 10:00-10:30 is blocked 09:45-10:45. The half-hour slots either side of
        // it, 09:30-10:00 and 10:30-11:00, only TOUCH the lecture itself — only the buffer
        // rules them out. Drop the buffer and both would be offered.
        await AddClassAsync(await RoomOfAsync(assetId), Local(day, 10), Local(day, 10, 30));

        var starts = (await AvailableAsync(manager, assetId, day, day, 30)).Select(s => s.StartsAt).ToList();

        Assert.DoesNotContain(Local(day, 9, 30), starts);
        Assert.DoesNotContain(Local(day, 10, 30), starts);

        // Clear of the buffer on both sides.
        Assert.Contains(Local(day, 9), starts);
        Assert.Contains(Local(day, 11), starts);
    }

    [Fact]
    public async Task AWeekendOnlyRange_OffersNothing_AndAWeekendInTheMiddleIsSkipped()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var assetId = await NewAssetAsync();
        var monday = FutureMonday();
        var friday = monday.AddDays(4);
        var saturday = monday.AddDays(5);
        var sunday = monday.AddDays(6);
        var nextMonday = monday.AddDays(7);

        // An empty list is a 200: "nothing free" is an answer, not an error.
        Assert.Empty(await AvailableAsync(manager, assetId, saturday, sunday, 60));

        // Four-hour slots: eleven a day (08:00 to 13:00 on the half hour), so the twenty-slot
        // cap is not used up on Friday and has to look past the weekend to fill.
        var spanning = await AvailableAsync(manager, assetId, friday, nextMonday, 240);
        var days = spanning.Select(s => LocalDay(s.StartsAt)).Distinct().ToList();

        Assert.Equal(new[] { friday, nextMonday }, days);
    }

    [Fact]
    public async Task Booking_ReRunsTheSameBoundary_TouchingIsAccepted_AMinuteInIs409()
    {
        var (manager, _) = await ClientAsync(Role.FacilitiesManager);
        var (_, technicianId) = await ClientAsync(Role.Technician);
        var (orderId, assetId) = await ApprovedAndAssignedAsync(manager, technicianId);
        var day = FutureMonday();

        await AddClassAsync(await RoomOfAsync(assetId), Local(day, 10, 15), Local(day, 10, 45));

        // One minute into the buffer: a taken slot, not a malformed one — 409, nothing booked.
        var oneMinuteIn = await manager.PostAsJsonAsync($"/api/workorders/{orderId}/schedule",
            new ScheduleWorkOrderDto(Local(day, 9, 1), Local(day, 10, 1)), JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, oneMinuteIn.StatusCode);

        // Ending exactly at the buffer's start is free, at booking exactly as at offering.
        var touching = await manager.PostAsJsonAsync($"/api/workorders/{orderId}/schedule",
            new ScheduleWorkOrderDto(Local(day, 9), Local(day, 10)), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, touching.StatusCode);

        var detail = await manager.GetFromJsonAsync<WorkOrderDetailDto>($"/api/workorders/{orderId}", JsonOptions);
        var slot = Assert.Single(detail!.ScheduledSlots);
        Assert.Equal(Local(day, 9), slot.StartsAt);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@campus.test", "SlotPass1", "Test User", role),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId);
    }

    /// <summary>An asset in a room of its own, so no other test's classes can reach it.</summary>
    private async Task<int> NewAssetAsync()
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

        return asset!.Id;
    }

    /// <summary>An auto-approved order on a fresh asset, assigned — ready to book.</summary>
    private async Task<(int OrderId, int AssetId)> ApprovedAndAssignedAsync(HttpClient manager, int technicianId)
    {
        var assetId = await NewAssetAsync();
        var roomId = await RoomOfAsync(assetId);

        var (reporter, _) = await ClientAsync(Role.Reporter);
        var report = await (await reporter.PostAsJsonAsync(
                "/api/reports", new CreateReportDto("Projector keeps cutting out mid-lecture.", roomId), JsonOptions))
            .Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        var raised = await manager.PostAsJsonAsync("/api/workorders",
            new CreateWorkOrderDto(report!.Id, assetId, WorkOrderStrategy.SingleJob, 500m, null), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var order = await raised.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);

        var assigned = await manager.PutAsJsonAsync(
            $"/api/workorders/{order!.Id}/assign", new AssignTechnicianDto(technicianId), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        return (order.Id, assetId);
    }

    private static async Task<List<AvailableSlotDto>> AvailableAsync(
        HttpClient manager, int assetId, DateOnly from, DateOnly to, int durationMinutes)
    {
        var url = $"/api/workorders/slots/available?assetId={assetId}&durationMinutes={durationMinutes}"
                + $"&fromDate={from:yyyy-MM-dd}&toDate={to:yyyy-MM-dd}";

        var response = await manager.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<AvailableSlotDto>>(JsonOptions))!;
    }

    private async Task<int> RoomOfAsync(int assetId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Assets.Where(a => a.Id == assetId).Select(a => a.RoomId).SingleAsync();
    }

    private async Task AddClassAsync(int roomId, DateTime startsAt, DateTime endsAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ClassScheduleSlots.Add(new ClassScheduleSlot
        {
            RoomId = roomId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Title = "SE3090 Lecture",
            ExternalEventId = $"evt-{Guid.NewGuid():N}",
            SyncedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    /// <summary>A Monday at least a week out, so nothing on it is ever "in the past".</summary>
    private static DateOnly FutureMonday()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private TimeZoneInfo CampusZone => _factory.Services.GetRequiredService<SchedulingSettings>().TimeZone;

    /// <summary>A campus-local wall-clock time, as the UTC instant the API stores.</summary>
    private DateTime Local(DateOnly day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), CampusZone);

    /// <summary>The campus-local calendar day a UTC instant falls on.</summary>
    private DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), CampusZone));
}
