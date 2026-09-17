namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Response DTO for one booked visit on a work order. Entities are never returned from a
/// controller directly, and a ScheduledSlot entity would drag its whole WorkOrder back
/// through its navigation property.
///
/// Distinct from <see cref="AvailableSlotDto"/>, which looks identical and means the
/// opposite: this is time that IS booked and has an id to prove it, that is time that
/// could be. Collapsing them would leave a client unable to tell an offer from a booking.
/// </summary>
public record ScheduledSlotDto(
    int Id,
    int WorkOrderId,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
