namespace CampusFacilities.Api.Models;

/// <summary>
/// A block of time booked for a <see cref="WorkOrder"/> — one visit.
///
/// Its own table rather than two columns on the work order: a job can take more than one
/// visit, and rescheduling then adds a row instead of overwriting the only record of when
/// the work was meant to happen.
///
/// Whether a slot is FREE is the other half of the question, and it is arithmetic over
/// these rows and <see cref="ClassScheduleSlot"/> — a technician cannot be in two places
/// at once, and nobody works on a projector during the lecture it is projecting. That
/// overlap check is a deterministic business rule and lives in C#. An agent may propose a
/// time; it never decides that a time is free.
/// </summary>
public class ScheduledSlot
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }

    public WorkOrder? WorkOrder { get; set; }

    /// <summary>
    /// UTC, and a DateTime rather than the DateOnly the asset registry uses: a service
    /// visit is a calendar date, but a booking is a time of day and has to be comparable
    /// against a lecture that starts at half past. Npgsql maps it to timestamptz.
    /// </summary>
    public DateTime StartsAt { get; set; }

    /// <summary>
    /// Exclusive end of the block, so back-to-back slots do not read as overlapping. That
    /// an end must follow its start is a rule for the service, not a database constraint.
    /// </summary>
    public DateTime EndsAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
