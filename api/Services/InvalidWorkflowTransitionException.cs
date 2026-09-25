using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// A move the workflow state machine does not allow. Thrown by
/// <see cref="WorkflowTransitions.Move"/> — and by AppDbContext on save, for a state changed
/// without going through it.
///
/// ExceptionHandlingMiddleware turns it into a 409 on any request: nothing about the request
/// is malformed, it is the workflow that is not where the caller thinks it is — the same
/// reasoning as an illegal report status move. Thrown rather than returned because it can
/// surface from every service that moves a workflow, and a typed outcome in each of them
/// would be one more status code for each controller to remember.
/// </summary>
public class InvalidWorkflowTransitionException : InvalidOperationException
{
    /// <summary>An event that may not happen in the workflow's current state.</summary>
    public InvalidWorkflowTransitionException(int workflowId, WorkflowState from, WorkflowTrigger trigger)
        : base($"Workflow {workflowId} is {from}; {trigger} cannot happen there.")
    {
        WorkflowId = workflowId;
        From = from;
        Trigger = trigger;
    }

    /// <summary>A state written directly that no trigger could have produced — AppDbContext's check.</summary>
    public InvalidWorkflowTransitionException(int workflowId, WorkflowState from, WorkflowState to)
        : base($"Workflow {workflowId} is {from} and cannot move to {to}.")
    {
        WorkflowId = workflowId;
        From = from;
        To = to;
    }

    public int WorkflowId { get; }

    public WorkflowState From { get; }

    /// <summary>Set when the refusal came from <see cref="WorkflowTransitions.Move"/>.</summary>
    public WorkflowTrigger? Trigger { get; }

    /// <summary>Set when the refusal came from the save-time check.</summary>
    public WorkflowState? To { get; }
}
