namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO — a block of time a work order COULD be booked into: one during which the
/// technician has no other visit and the room has no class.
///
/// It carries no id because it is not a row. These are computed in C# by subtracting the
/// technician's ScheduledSlots and the room's ClassScheduleSlots from the working day, and
/// they exist only in the answer to one question at one moment. Booking one creates a
/// ScheduledSlot, which is a row and does have an id — see <see cref="ScheduledSlotDto"/>,
/// which looks identical and means the opposite.
///
/// Offering a slot is therefore never a promise: two managers can be shown the same free
/// block, so the conflict check is re-run on the booking itself rather than trusted from
/// whenever this list was produced.
/// </summary>
public record AvailableSlotDto(
    DateTime StartsAt,
    DateTime EndsAt);
