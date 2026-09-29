namespace CampusFacilities.Api.Services;

/// <summary>
/// What HAPPENED to a workflow — the labels on the arrows of DEVELOPMENT_GUIDE.md §8.
/// <see cref="WorkflowTransitions"/> is keyed by the state AND the trigger, not by the pair
/// of states, because the same pair can be reached by events that must not stand in for
/// each other: AwaitingManagerApproval -> WorkOrderRaised is a manager APPROVING, and raising
/// a second order on the same report must not be able to take that edge and read as an
/// approval nobody gave.
///
/// Not persisted: the state is what is stored; the trigger is what the caller says it is
/// doing, checked against the table at the moment it does it.
/// </summary>
public enum WorkflowTrigger
{
    /// <summary>The clarifier asked the reporter at least one question.</summary>
    ClarifierAsked,

    /// <summary>The clarifier found nothing that needed asking.</summary>
    ClarifierFoundNothing,

    /// <summary>
    /// The planner judged the report clear enough to go straight to diagnosis, so the
    /// clarifier was never delegated a step. Its own trigger rather than ClarifierFoundNothing:
    /// the two reach the same state, but "the clarifier ran and asked nothing" and "the
    /// clarifier did not run" are different facts, and a table keyed by trigger keeps them apart.
    /// </summary>
    PlannedWithoutClarification,

    /// <summary>The reporter submitted the clarification form.</summary>
    ReporterAnswered,

    /// <summary>The diagnostic has run, with or without a diagnosis.</summary>
    Diagnosed,

    /// <summary>A work order was raised at or under the threshold, not a replacement.</summary>
    WorkOrderAutoApproved,

    /// <summary>A work order was raised over the threshold, or as a replacement.</summary>
    WorkOrderNeedsApproval,

    ManagerApproved,
    ManagerRejected,
    RevisionRequested,

    /// <summary>A technician started the job. Nothing fires this yet — there is no start endpoint.</summary>
    WorkStarted,

    /// <summary>The assigned technician completed the job.</summary>
    WorkCompleted,

    /// <summary>The verification sweep: the repair is VerificationSettings.DelayDays old.</summary>
    VerificationDue,

    /// <summary>The repair held. Not fired yet — see WorkflowTransitions.</summary>
    RepairVerified,

    /// <summary>The fault came back. Not fired yet.</summary>
    RepairReopened,

    /// <summary>The fault is a pattern for a manager. Not fired yet.</summary>
    RepairEscalated,

    /// <summary>The agent run could not continue: the service was unreachable, or the clarifier safe-failed.</summary>
    AgentFailed
}
