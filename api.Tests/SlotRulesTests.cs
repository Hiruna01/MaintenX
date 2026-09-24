using CampusFacilities.Api.Services;
using Xunit;

namespace api.Tests;

/// <summary>
/// SlotRules, called directly — no database, no request. Off-by-one errors on boundary
/// overlaps are the classic scheduling bug and they are invisible until a demo books a
/// technician into a lecture, so every boundary here is tested on the exact minute and one
/// minute either side of it.
///
/// Every time is written as CAMPUS-LOCAL (Asia/Colombo, UTC+05:30, the default) and
/// converted, so the tests read the way the rule is stated — "08:00 to 17:00" — and would
/// catch a rule that quietly compared against the UTC clock instead. 5 October 2026 is a
/// Monday; 10 October a Saturday.
/// </summary>
public class SlotRulesTests
{
    private static readonly SchedulingSettings Settings = new();

    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Saturday = new(2026, 10, 10);

    /// <summary>A campus-local wall-clock time on <paramref name="day"/>, as UTC.</summary>
    private static DateTime At(DateOnly day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), Settings.TimeZone);

    private static DateTime Mon(int hour, int minute = 0) => At(Monday, hour, minute);

    /// <summary>Well before any test date, so nothing is rejected as being in the past.</summary>
    private static readonly DateTime LongAgo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly BusyInterval[] Nothing = Array.Empty<BusyInterval>();

    // ---------------------------------------------------------------------------------
    // Overlaps — against an existing block of 10:00-11:00
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(9, 0, 10, 0, false)]    // ends exactly as it starts: touching, not overlapping
    [InlineData(11, 0, 12, 0, false)]   // starts exactly as it ends: touching, not overlapping
    [InlineData(9, 0, 10, 1, true)]     // one minute into it
    [InlineData(10, 59, 12, 0, true)]   // one minute before it ends
    [InlineData(10, 15, 10, 45, true)]  // inside it
    [InlineData(9, 0, 12, 0, true)]     // around it
    [InlineData(10, 0, 11, 0, true)]    // exactly it
    [InlineData(8, 0, 9, 0, false)]     // well before
    [InlineData(12, 0, 13, 0, false)]   // well after
    public void Overlaps_TouchingIsNotOverlapping_AndOneMinuteIs(
        int startHour, int startMinute, int endHour, int endMinute, bool expected)
    {
        var candidateStart = Mon(startHour, startMinute);
        var candidateEnd = Mon(endHour, endMinute);

        Assert.Equal(expected, SlotRules.Overlaps(Mon(10), Mon(11), candidateStart, candidateEnd));

        // The test is symmetric: which block is "existing" must not change the answer.
        Assert.Equal(expected, SlotRules.Overlaps(candidateStart, candidateEnd, Mon(10), Mon(11)));
    }

    [Fact]
    public void Overlaps_ASingleTickIsEnough()
    {
        Assert.True(SlotRules.Overlaps(Mon(10), Mon(11), Mon(9), Mon(10).AddTicks(1)));
        Assert.False(SlotRules.Overlaps(Mon(10), Mon(11), Mon(9), Mon(10)));
    }

    // ---------------------------------------------------------------------------------
    // Working hours
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(8, 0, 9, 0, true)]      // opens at 08:00
    [InlineData(7, 59, 8, 59, false)]   // a minute before opening
    [InlineData(16, 0, 17, 0, true)]    // ends exactly at closing: inside
    [InlineData(16, 1, 17, 1, false)]   // a minute past closing
    [InlineData(8, 0, 17, 0, true)]     // the whole day
    [InlineData(10, 0, 10, 0, false)]   // zero length
    [InlineData(11, 0, 10, 0, false)]   // ends before it starts
    public void WorkingHours_AreCampusLocal_AndClosingTimeIsInclusive(
        int startHour, int startMinute, int endHour, int endMinute, bool expected)
    {
        Assert.Equal(expected, SlotRules.IsWithinWorkingHours(
            Mon(startHour, startMinute), Mon(endHour, endMinute), Settings));
    }

    [Fact]
    public void WorkingHours_AreNotTheUtcClock()
    {
        // 08:00 UTC is 13:30 in Colombo — fine. But 08:00 LOCAL is 02:30 UTC, and a rule
        // comparing against the UTC clock would refuse it.
        var localEight = Mon(8);
        Assert.Equal(new DateTime(2026, 10, 5, 2, 30, 0, DateTimeKind.Utc), localEight);
        Assert.True(SlotRules.IsWithinWorkingHours(localEight, localEight.AddHours(1), Settings));

        // And 16:00 UTC is 21:30 local: long after closing, though inside "08:00-17:00 UTC".
        var utcAfternoon = new DateTime(2026, 10, 5, 16, 0, 0, DateTimeKind.Utc);
        Assert.False(SlotRules.IsWithinWorkingHours(utcAfternoon, utcAfternoon.AddMinutes(30), Settings));
    }

    [Fact]
    public void WorkingHours_ExcludeTheWeekend()
    {
        Assert.False(SlotRules.IsWithinWorkingHours(At(Saturday, 10), At(Saturday, 11), Settings));
        Assert.False(SlotRules.IsWithinWorkingHours(At(Saturday.AddDays(1), 10), At(Saturday.AddDays(1), 11), Settings));
    }

    // ---------------------------------------------------------------------------------
    // The class buffer — a class at 10:00-11:00 blocks 09:45-11:15
    // ---------------------------------------------------------------------------------

    // Expected as a bool rather than a SlotCheck: a public test method cannot take an
    // internal type as a parameter.
    [Theory]
    [InlineData(8, 45, 9, 45, true)]     // ends exactly as the buffer starts
    [InlineData(8, 46, 9, 46, false)]    // a minute into the buffer
    [InlineData(11, 15, 12, 15, true)]   // starts exactly as the buffer ends
    [InlineData(11, 14, 12, 14, false)]  // a minute before it ends
    [InlineData(10, 0, 11, 0, false)]    // during the class itself
    public void ClassBuffer_IsFifteenMinutesEitherSide_ToTheMinute(
        int startHour, int startMinute, int endHour, int endMinute, bool free)
    {
        var busy = new[] { SlotRules.ClassWithBuffer(Mon(10), Mon(11), Settings) };

        Assert.Equal(free ? SlotCheck.Free : SlotCheck.Conflict, SlotRules.Check(
            Mon(startHour, startMinute), Mon(endHour, endMinute), busy, LongAgo, Settings));
    }

    [Fact]
    public void TechnicianVisits_HaveNoBuffer()
    {
        // Back-to-back visits by the same technician are fine; only classes get the buffer.
        var busy = new[] { new BusyInterval(Mon(13), Mon(14)) };

        Assert.Equal(SlotCheck.Free, SlotRules.Check(Mon(12), Mon(13), busy, LongAgo, Settings));
        Assert.Equal(SlotCheck.Free, SlotRules.Check(Mon(14), Mon(15), busy, LongAgo, Settings));
        Assert.Equal(SlotCheck.Conflict, SlotRules.Check(Mon(12, 30), Mon(13, 30), busy, LongAgo, Settings));
    }

    [Fact]
    public void Check_ReportsShapeBeforeTimeBeforeConflict()
    {
        // The order is what lets booking tell "never bookable" (400) from "taken" (409).
        var busy = new[] { new BusyInterval(Mon(6), Mon(20)) };

        Assert.Equal(SlotCheck.OutsideWorkingHours, SlotRules.Check(Mon(7), Mon(8), busy, LongAgo, Settings));
        Assert.Equal(SlotCheck.InThePast, SlotRules.Check(Mon(9), Mon(10), busy, Mon(9, 1), Settings));
        Assert.Equal(SlotCheck.Conflict, SlotRules.Check(Mon(9), Mon(10), busy, LongAgo, Settings));
    }

    [Fact]
    public void Check_AStartExactlyNowIsNotInThePast()
    {
        Assert.Equal(SlotCheck.Free, SlotRules.Check(Mon(9), Mon(10), Nothing, Mon(9), Settings));
        Assert.Equal(SlotCheck.InThePast, SlotRules.Check(Mon(9), Mon(10), Nothing, Mon(9).AddTicks(1), Settings));
    }

    // ---------------------------------------------------------------------------------
    // FindFreeSlots
    // ---------------------------------------------------------------------------------

    [Fact]
    public void FindFreeSlots_AnEmptyDay_RunsFromOpeningToAVisitEndingAtClosing()
    {
        var slots = SlotRules.FindFreeSlots(Monday, Monday, TimeSpan.FromHours(1), Nothing, LongAgo, Settings);

        // 08:00, 08:30 ... 16:00 — seventeen, the last ending exactly at 17:00. A loop
        // bound written as "start + duration < closes" would lose that last one.
        Assert.Equal(17, slots.Count);
        Assert.Equal(Mon(8), slots[0].StartsAt);
        Assert.Equal(Mon(16), slots[^1].StartsAt);
        Assert.Equal(Mon(17), slots[^1].EndsAt);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromHours(1), s.EndsAt - s.StartsAt));
    }

    [Fact]
    public void FindFreeSlots_StepsAroundABufferedClass()
    {
        var busy = new[] { SlotRules.ClassWithBuffer(Mon(10), Mon(11), Settings) };

        var starts = SlotRules
            .FindFreeSlots(Monday, Monday, TimeSpan.FromHours(1), busy, LongAgo, Settings)
            .Select(s => s.StartsAt)
            .ToList();

        // 08:30-09:30 clears the buffer (09:45); 09:00-10:00 does not. 11:00 starts inside
        // the buffer (to 11:15); 11:30 is the next grid point and is clear.
        Assert.Equal(
            new[] { Mon(8), Mon(8, 30), Mon(11, 30), Mon(12), Mon(12, 30), Mon(13), Mon(13, 30),
                    Mon(14), Mon(14, 30), Mon(15), Mon(15, 30), Mon(16) },
            starts);
    }

    [Fact]
    public void FindFreeSlots_SkipsTheWeekend()
    {
        Assert.Empty(SlotRules.FindFreeSlots(
            Saturday, Saturday.AddDays(1), TimeSpan.FromHours(1), Nothing, LongAgo, Settings));

        // Saturday to Monday offers Monday only.
        var slots = SlotRules.FindFreeSlots(
            Saturday, Saturday.AddDays(2), TimeSpan.FromHours(1), Nothing, LongAgo, Settings);
        Assert.All(slots, s => Assert.Equal(new DateOnly(2026, 10, 12), DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(s.StartsAt, Settings.TimeZone))));
    }

    [Fact]
    public void FindFreeSlots_OffersNothingAlreadyStarted()
    {
        var slots = SlotRules.FindFreeSlots(
            Monday, Monday, TimeSpan.FromHours(1), Nothing, Mon(12, 10), Settings);

        Assert.Equal(Mon(12, 30), slots[0].StartsAt);
    }

    [Fact]
    public void FindFreeSlots_StopsAtTwenty()
    {
        var slots = SlotRules.FindFreeSlots(
            Monday, Monday.AddDays(4), TimeSpan.FromHours(1), Nothing, LongAgo, Settings);

        Assert.Equal(SlotRules.MaxAvailableSlots, slots.Count);
        Assert.Equal(20, slots.Count);

        // Earliest first: all of Monday (17), then the start of Tuesday.
        Assert.Equal(At(Monday.AddDays(1), 9), slots[^1].StartsAt);
    }

    [Fact]
    public void FindFreeSlots_NeverOffersAnythingCheckWouldRefuse()
    {
        // Offering and booking share one rule; this is the property that follows from it.
        var busy = new[]
        {
            SlotRules.ClassWithBuffer(Mon(9), Mon(10), Settings),
            SlotRules.ClassWithBuffer(Mon(14, 10), Mon(15, 50), Settings),
            new BusyInterval(Mon(12), Mon(12, 45))
        };

        var slots = SlotRules.FindFreeSlots(
            Monday, Monday.AddDays(4), TimeSpan.FromMinutes(45), busy, LongAgo, Settings);

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.Equal(
            SlotCheck.Free, SlotRules.Check(s.StartsAt, s.EndsAt, busy, LongAgo, Settings)));
    }
}
