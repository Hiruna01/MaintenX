using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// THE WORKFLOW STATE MACHINE — every legal move, in one place (DEVELOPMENT_GUIDE.md §8).
///
///   Submitted ──┬─ clarifier asked ─────────► AwaitingClarification ── reporter answered ──┐
///               └─ clarifier found nothing ──► Diagnosing ◄───────────────────────────────┘
///   Diagnosing ── diagnosed ──► Strategizing ──┬─ order auto-approved ──► WorkOrderRaised
///                                              └─ order needs approval ─► AwaitingManagerApproval
///   AwaitingManagerApproval ── approved ► WorkOrderRaised · rejected ► Closed · revision ► Strategizing
///   WorkOrderRaised ── started ► InProgress ── completed ► Completed ── verification due ► AwaitingVerification
///   AwaitingVerification ── verified ► Closed · reopened ► Diagnosing · escalated ► AwaitingManagerApproval
///
/// A hardcoded table, the same instinct as the tool allow-list and the report lifecycle: a
/// state that could go anywhere is not a lifecycle, it is a free-text field wearing an enum's
/// name. Keyed by (state, trigger) rather than by two states, so each arrow is only taken by
/// the event it is drawn for — see <see cref="WorkflowTrigger"/>. Which state follows which
/// is a deterministic business rule, so it is C#, and no agent proposes, validates or applies
/// a transition.
///
/// Every service that moves a workflow calls <see cref="Move"/>; none assigns CurrentState
/// itself. AppDbContext also checks every changed state against <see cref="CanReach"/> on
/// save, so a direct assignment that skipped this class is refused anyway.
/// </summary>
public static class WorkflowTransitions
{
    private static readonly IReadOnlyDictionary<(WorkflowState From, WorkflowTrigger Trigger), WorkflowState> Table =
        new Dictionary<(WorkflowState, WorkflowTrigger), WorkflowState>
        {
            // The clarifier runs while the workflow is still Submitted, and what it says
            // decides the next state — see ForClarification.
            [(WorkflowState.Submitted, WorkflowTrigger.ClarifierAsked)] = WorkflowState.AwaitingClarification,
            [(WorkflowState.Submitted, WorkflowTrigger.ClarifierFoundNothing)] = WorkflowState.Diagnosing,
            [(WorkflowState.Submitted, WorkflowTrigger.AgentFailed)] = WorkflowState.Failed,

            // Human pause 1. Only the reporter's answers move it on.
            [(WorkflowState.AwaitingClarification, WorkflowTrigger.ReporterAnswered)] = WorkflowState.Diagnosing,

            [(WorkflowState.Diagnosing, WorkflowTrigger.Diagnosed)] = WorkflowState.Strategizing,
            [(WorkflowState.Diagnosing, WorkflowTrigger.AgentFailed)] = WorkflowState.Failed,

            // The workflow waits here, holding the strategist's proposal, until a manager
            // raises the order; the order's own approval gate picks the trigger.
            [(WorkflowState.Strategizing, WorkflowTrigger.WorkOrderAutoApproved)] = WorkflowState.WorkOrderRaised,
            [(WorkflowState.Strategizing, WorkflowTrigger.WorkOrderNeedsApproval)] = WorkflowState.AwaitingManagerApproval,
            [(WorkflowState.Strategizing, WorkflowTrigger.AgentFailed)] = WorkflowState.Failed,

            // Human pause 2.
            [(WorkflowState.AwaitingManagerApproval, WorkflowTrigger.ManagerApproved)] = WorkflowState.WorkOrderRaised,
            [(WorkflowState.AwaitingManagerApproval, WorkflowTrigger.ManagerRejected)] = WorkflowState.Closed,
            [(WorkflowState.AwaitingManagerApproval, WorkflowTrigger.RevisionRequested)] = WorkflowState.Strategizing,

            // Completed straight from WorkOrderRaised as well as through InProgress: nothing
            // starts a job yet (no endpoint moves a work order to InProgress), and a job the
            // technician finished without pressing "start" is still finished. Same shape as
            // a report jumping Submitted -> Diagnosed when nothing needed clarifying.
            [(WorkflowState.WorkOrderRaised, WorkflowTrigger.WorkStarted)] = WorkflowState.InProgress,
            [(WorkflowState.WorkOrderRaised, WorkflowTrigger.WorkCompleted)] = WorkflowState.Completed,
            [(WorkflowState.InProgress, WorkflowTrigger.WorkCompleted)] = WorkflowState.Completed,

            // Only the verification sweep moves it on, and only once VerificationSettings
            // DelayDays have passed — asked the same afternoon, every reporter says yes.
            [(WorkflowState.Completed, WorkflowTrigger.VerificationDue)] = WorkflowState.AwaitingVerification,

            // The two loops that make this more than a helpdesk: a repair that did not hold
            // goes back to Diagnosing, and a pattern goes to a manager. Nothing fires these
            // yet — the VerificationAgent's C# runner does not exist — but they are the rule
            // it will be held to.
            [(WorkflowState.AwaitingVerification, WorkflowTrigger.RepairVerified)] = WorkflowState.Closed,
            [(WorkflowState.AwaitingVerification, WorkflowTrigger.RepairReopened)] = WorkflowState.Diagnosing,
            [(WorkflowState.AwaitingVerification, WorkflowTrigger.RepairEscalated)] = WorkflowState.AwaitingManagerApproval,

            // Closed has no way out. A fault that returns is a new report with its own workflow.

            // The agent failing costs advice, never the ability to act: a manager can still
            // raise an order by hand for a report whose run failed, and the order's gate
            // routes it exactly as it would have from Strategizing.
            [(WorkflowState.Failed, WorkflowTrigger.WorkOrderAutoApproved)] = WorkflowState.WorkOrderRaised,
            [(WorkflowState.Failed, WorkflowTrigger.WorkOrderNeedsApproval)] = WorkflowState.AwaitingManagerApproval
        };

    /// <summary>Every legal move, for a test to pin and a reader to list.</summary>
    public static IEnumerable<(WorkflowState From, WorkflowTrigger Trigger, WorkflowState To)> All =>
        Table.Select(t => (t.Key.From, t.Key.Trigger, t.Value));

    /// <summary>Where <paramref name="trigger"/> takes a workflow in <paramref name="from"/>, or null if it may not happen there.</summary>
    public static WorkflowState? Next(WorkflowState from, WorkflowTrigger trigger) =>
        Table.TryGetValue((from, trigger), out var to) ? to : null;

    /// <summary>
    /// True when SOME trigger takes <paramref name="from"/> to <paramref name="to"/>. What
    /// AppDbContext checks on save, where the state change is visible and the event is not.
    /// </summary>
    public static bool CanReach(WorkflowState from, WorkflowState to) =>
        Table.Any(t => t.Key.From == from && t.Value == to);

    /// <summary>
    /// Moves the workflow, or throws <see cref="InvalidWorkflowTransitionException"/> and
    /// leaves it untouched. No trigger leaves a state where it is, so a caller asserting an
    /// event that has already happened is refused rather than given a silent success — its
    /// view is stale, and saying so is more use.
    /// </summary>
    public static void Move(AgentWorkflow workflow, WorkflowTrigger trigger)
    {
        var to = Next(workflow.CurrentState, trigger)
                 ?? throw new InvalidWorkflowTransitionException(workflow.Id, workflow.CurrentState, trigger);

        workflow.CurrentState = to;
    }

    /// <summary>What the clarifier's answer means: pause if it asked anything.</summary>
    public static WorkflowTrigger ForClarification(int questionCount) =>
        questionCount > 0 ? WorkflowTrigger.ClarifierAsked : WorkflowTrigger.ClarifierFoundNothing;

    /// <summary>
    /// What raising a work order means for its workflow. Whether the order needs a manager is
    /// WorkOrderService.ApprovalBasisFor — the threshold is its business, not this class's.
    /// </summary>
    public static WorkflowTrigger ForRaisedWorkOrder(bool requiresApproval) =>
        requiresApproval ? WorkflowTrigger.WorkOrderNeedsApproval : WorkflowTrigger.WorkOrderAutoApproved;
}
