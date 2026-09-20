namespace CampusFacilities.Api.Models;

/// <summary>
/// Where a <see cref="VerificationCheck"/> has got to. Persisted as a string in PostgreSQL
/// (see AppDbContext), same as <see cref="Role"/> and <see cref="WorkOrderStatus"/>, so a
/// row reads "AwaitingReporterResponse" rather than "1".
///
/// This tracks a QUESTION, not work. A work order says the technician finished; this says
/// whether the person who reported the fault agrees it is actually gone. The two disagree
/// often enough to be worth a table — a job marked Completed and a projector still cutting
/// out are entirely compatible, and nothing before this component noticed.
///
/// Every transition is a deterministic business rule and lives in C#: when a check falls
/// due, what a yes or a no means, when an unanswered check gives up. An agent may write
/// its opinion into AgentOutcome; it never sets this column.
/// </summary>
public enum VerificationStatus
{
    /// <summary>
    /// Created when the work order completed, waiting for DueAt to arrive. Nobody has been
    /// asked anything yet — the delay exists so the fault has a chance to come back.
    /// </summary>
    Pending,

    /// <summary>
    /// DueAt has passed and the sweep has put the question to the reporter. Waiting on a
    /// human, which is why this is distinct from Pending: the difference between "not yet
    /// asked" and "asked, no reply" is the difference between the system being slow and
    /// the reporter being slow.
    /// </summary>
    AwaitingReporterResponse,

    /// <summary>The reporter says the fault is gone. The happy ending, and terminal.</summary>
    Confirmed,

    /// <summary>
    /// The reporter says it is still broken. Terminal for THIS check — the fault itself is
    /// not finished, and a new work order is what follows, carrying its own check.
    /// </summary>
    Reopened,

    /// <summary>
    /// Raised for a human to look at rather than answered by one — a fault that has been
    /// reopened before, or a check on equipment whose history already shows a repeat
    /// failure. Deliberately not the same as Reopened: this says the loop itself is not
    /// working, not merely that one repair did not hold.
    /// </summary>
    Escalated,

    /// <summary>
    /// Asked, never answered, and no longer worth waiting on. Distinct from Confirmed on
    /// purpose: silence is not agreement, and counting it as one would let the confirmation
    /// rate in <see cref="Dtos.MetricsDto"/> report a success nobody reported.
    /// </summary>
    Expired
}
