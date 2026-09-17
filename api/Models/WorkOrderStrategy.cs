namespace CampusFacilities.Api.Models;

/// <summary>
/// How the work is to be approached. Persisted as a string in PostgreSQL (see
/// AppDbContext), same as every other enum in this project.
///
/// This is the shape of the RECOMMENDATION the planning agent produces, which is why it
/// is a closed list rather than free text: an agent that could describe a strategy in
/// prose would be making the decision in a form nothing downstream could act on, and this
/// project has no chat interface to read prose in. The agent chooses a member; what that
/// member MEANS for cost, approval routing and scheduling is C#.
/// </summary>
public enum WorkOrderStrategy
{
    /// <summary>
    /// This exact fault on this exact equipment has been fixed before, and the service
    /// history says how. The cheapest outcome, and the reason the history is kept.
    /// </summary>
    KnownFix,

    /// <summary>One visit, one fault, nothing else attached to it.</summary>
    SingleJob,

    /// <summary>
    /// Folded in with other work — several faults in one room, or the same fault across a
    /// corridor — so one visit clears them all. Whether consolidating is actually cheaper
    /// is arithmetic over estimated costs, and that arithmetic is C#.
    /// </summary>
    ConsolidatedJob,

    /// <summary>
    /// The fault is not understood well enough to quote. A technician looks first, and
    /// whatever they find raises a second work order against the same report.
    /// </summary>
    InspectFirst,

    /// <summary>
    /// Deliberately postponed — out of warranty work that can wait, or a room in use until
    /// the end of term. A decision that was made, not one that was missed.
    /// </summary>
    Defer,

    /// <summary>
    /// Repairing it again is no longer the right answer: the repeat-failure pattern in the
    /// service history says replace. Whether the pattern is real is a failure count over
    /// ServiceRecord rows — deterministic, and therefore C#, not a judgement made in a
    /// prompt.
    /// </summary>
    EscalateReplacement
}
