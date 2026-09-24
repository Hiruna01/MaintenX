using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

/// <summary>
/// Whether a block of time is free to book maintenance into — the deterministic rule behind
/// both GET /api/workorders/slots/available and POST /api/workorders/{id}/schedule.
///
/// PURE FUNCTIONS, NO DATABASE. WorkOrderService loads the busy times and the clock; this
/// class decides. That split is what lets the boundary cases — a slot ending the minute a
/// buffer starts, a visit ending exactly at 17:00 — be unit tested directly, without a
/// database or a request, because off-by-one errors on boundary overlaps are invisible until
/// a demo books a technician into a lecture.
///
/// OFFERING AND BOOKING RUN THE SAME CHECK. <see cref="FindFreeSlots"/> keeps a candidate
/// only if <see cref="Check"/> says Free, and booking calls <see cref="Check"/> again on the
/// submitted slot. There is no second copy of the rule for the booking side to disagree with.
///
/// INTERNAL, not private: the test project sees it through InternalsVisibleTo, and nothing
/// outside this assembly does. A private method could only be tested through reflection.
///
/// Times are UTC throughout. The only conversion to campus-local time is to answer "is this
/// inside the working day", in <see cref="IsWithinWorkingHours"/>.
/// </summary>
internal static class SlotRules
{
    /// <summary>The most slots one request offers.</summary>
    internal const int MaxAvailableSlots = 20;

    /// <summary>
    /// Candidates start on the hour and the half hour. Fine enough to use a gap after a
    /// buffered class, coarse enough that twenty offers span most of a day instead of
    /// twenty starts fifteen minutes apart on the same morning.
    /// </summary>
    internal const int SlotStepMinutes = 30;

    /// <summary>
    /// THE OVERLAP TEST, and the only one. Two blocks overlap when each starts before the
    /// other ends. Both comparisons are STRICT, so blocks that merely touch — one ends at
    /// 10:00, the next starts at 10:00 — do not overlap. That is what makes end times
    /// exclusive (see ScheduledSlot.EndsAt), and it is what lets back-to-back visits be
    /// booked at all.
    /// </summary>
    internal static bool Overlaps(
        DateTime existingStart,
        DateTime existingEnd,
        DateTime candidateStart,
        DateTime candidateEnd) =>
        existingStart < candidateEnd && existingEnd > candidateStart;

    /// <summary>
    /// A class as the room is actually unavailable: widened by the buffer on BOTH sides, so
    /// the overlap test above needs no special case for it.
    /// </summary>
    internal static BusyInterval ClassWithBuffer(DateTime startsAt, DateTime endsAt, SchedulingSettings settings) =>
        new(startsAt.AddMinutes(-settings.ClassBufferMinutes), endsAt.AddMinutes(settings.ClassBufferMinutes));

    /// <summary>
    /// Monday to Friday, starting no earlier than the workday start and ending no later than
    /// the workday end, both on the same campus-local day. An end exactly at closing time
    /// is inside it.
    /// </summary>
    internal static bool IsWithinWorkingHours(DateTime startUtc, DateTime endUtc, SchedulingSettings settings)
    {
        if (endUtc <= startUtc)
        {
            return false;
        }

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, settings.TimeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, settings.TimeZone);

        if (localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        // Both bounds on the day the visit STARTS. Because closing time is before midnight,
        // "ends no later than closing on the start day" also rules out a visit that runs
        // over into the next morning.
        var day = DateOnly.FromDateTime(localStart);

        return localStart >= day.ToDateTime(settings.WorkdayStart)
            && localEnd <= day.ToDateTime(settings.WorkdayEnd);
    }

    /// <summary>
    /// The whole decision for one block of time, in a fixed order: shape first (inside the
    /// working day), then time (not already started), then conflicts. The order is what lets
    /// booking tell a slot that was never bookable (400) from one that was taken (409).
    /// </summary>
    internal static SlotCheck Check(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyList<BusyInterval> busy,
        DateTime nowUtc,
        SchedulingSettings settings)
    {
        if (!IsWithinWorkingHours(startUtc, endUtc, settings))
        {
            return SlotCheck.OutsideWorkingHours;
        }

        if (startUtc < nowUtc)
        {
            return SlotCheck.InThePast;
        }

        foreach (var interval in busy)
        {
            if (Overlaps(interval.StartsAt, interval.EndsAt, startUtc, endUtc))
            {
                return SlotCheck.Conflict;
            }
        }

        return SlotCheck.Free;
    }

    /// <summary>
    /// Every free block of <paramref name="duration"/> between two campus-local calendar
    /// dates, both inclusive, earliest first, at most <see cref="MaxAvailableSlots"/>.
    ///
    /// Candidates are generated on a <see cref="SlotStepMinutes"/> grid from the start of
    /// each day and kept only if <see cref="Check"/> says Free — weekends, the end of the
    /// day, the past and every conflict are all rejected by that one call rather than by a
    /// second, subtly different loop condition.
    /// </summary>
    internal static IReadOnlyList<AvailableSlotDto> FindFreeSlots(
        DateOnly fromDate,
        DateOnly toDate,
        TimeSpan duration,
        IReadOnlyList<BusyInterval> busy,
        DateTime nowUtc,
        SchedulingSettings settings)
    {
        var slots = new List<AvailableSlotDto>();

        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            var opens = day.ToDateTime(settings.WorkdayStart);
            var closes = day.ToDateTime(settings.WorkdayEnd);

            for (var localStart = opens; localStart < closes; localStart = localStart.AddMinutes(SlotStepMinutes))
            {
                // A local time a daylight-saving jump skips does not exist to book. Colombo
                // has none, but the zone is configuration.
                if (settings.TimeZone.IsInvalidTime(localStart))
                {
                    continue;
                }

                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, settings.TimeZone);
                var endUtc = startUtc + duration;

                if (Check(startUtc, endUtc, busy, nowUtc, settings) != SlotCheck.Free)
                {
                    continue;
                }

                slots.Add(new AvailableSlotDto(startUtc, endUtc));

                if (slots.Count == MaxAvailableSlots)
                {
                    return slots;
                }
            }
        }

        return slots;
    }
}

/// <summary>A block of time something else already occupies. UTC.</summary>
internal readonly record struct BusyInterval(DateTime StartsAt, DateTime EndsAt);

/// <summary>What <see cref="SlotRules.Check"/> found about one block of time.</summary>
internal enum SlotCheck
{
    Free,

    /// <summary>A weekend, before opening, after closing, or an end not after its start.</summary>
    OutsideWorkingHours,

    /// <summary>Starts before now. An offer made earlier can go stale this way too.</summary>
    InThePast,

    /// <summary>Overlaps a buffered class in the room or another of the technician's visits.</summary>
    Conflict
}
