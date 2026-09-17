using System.ComponentModel.DataAnnotations;

namespace CampusFacilities.Api.Models;

/// <summary>
/// A teaching booking for a room, mirrored from the campus timetable.
///
/// This table exists so that "is this room free on Thursday afternoon?" can be answered in
/// SQL, by C#, against rows this system holds — rather than by asking a model to reason
/// about a calendar. Scheduling maintenance into an occupied lecture theatre is the
/// mistake it prevents, and a timetable conflict is a deterministic business rule: it is
/// two intervals overlapping, and nothing else.
///
/// Nothing here is authored by this system. Rows are synced in from the timetable that
/// owns them, which is why every row carries the external id it was synced from.
/// </summary>
public class ClassScheduleSlot
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>UTC, same reasoning as <see cref="ScheduledSlot.StartsAt"/>.</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>Exclusive end, so a class ending as another begins is not an overlap.</summary>
    public DateTime EndsAt { get; set; }

    /// <summary>
    /// What is on in the room — a module code, an exam, a booking reference. Shown to a
    /// manager choosing a slot, so they can see WHAT they would be working around rather
    /// than just that something is there.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The id this booking carries in the timetable system it came from. Unique, and that
    /// is what makes a re-sync idempotent: the same class pulled twice updates its row
    /// instead of appearing as a second lecture in the same room at the same time, which
    /// would read as a conflict that does not exist.
    ///
    /// Required, because every row here is mirrored from somewhere — nothing in this
    /// system authors a class.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string ExternalEventId { get; set; } = string.Empty;

    /// <summary>
    /// When this row was last pulled from the timetable. Distinct from
    /// <see cref="UpdatedAt"/>, which AppDbContext stamps whenever the row is written: a
    /// sync that finds nothing changed leaves UpdatedAt alone, so this is the only column
    /// that says how stale the mirror is.
    /// </summary>
    public DateTime SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
