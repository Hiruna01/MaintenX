using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — the slot a manager is booking, normally one of the AvailableSlotDto entries
/// GET /api/workorders/slots/available returned, sent back unchanged.
///
/// AN OFFER IS NOT A RESERVATION. The service re-runs the same availability check on these
/// two times at booking time, so a slot somebody else took after it was offered is a 409
/// rather than a double booking. See WorkOrderService.ScheduleAsync.
///
/// UTC, and it must SAY so: send the "Z" the available-slots response carries. A time with
/// no offset is refused with a 400 — guessing which clock it was read off is how a booking
/// lands five and a half hours from where the manager put it.
///
/// Nullable with [Required], so a missing time is a 400 and never 0001-01-01.
/// </summary>
public record ScheduleWorkOrderDto(
    [Required] DateTime? StartsAt,
    [Required] DateTime? EndsAt);
