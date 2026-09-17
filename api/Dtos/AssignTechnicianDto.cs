using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Input DTO — who is going to do the work.
///
/// Assignment only, with no time on it: choosing a slot is a separate step against
/// separate rows (ScheduledSlot), and it is the step that has to be checked for conflicts
/// against the technician's other bookings and the room's timetable. Folding a start and
/// end into this record would make "assign" quietly mean "assign and book", and a caller
/// that only wanted to hand the job to somebody else would have to invent a time.
///
/// That the named user is actually a Technician, and is free when the work is booked, are
/// deterministic business rules. They belong in the service in C#, not in a DataAnnotation
/// that can see nothing but this integer.
/// </summary>
public record AssignTechnicianDto(
    [Range(1, int.MaxValue)] int TechnicianId);
