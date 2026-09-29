using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

/// <summary>
/// WHAT A WORKFLOW MOVE MEANS FOR ITS REPORT — the one place that says so, and the reporter's
/// view of both.
///
/// A report's status says where the FAULT has got to; a workflow's state says where one agent
/// run has. They are different things (see ReportStatus), but some events move both: the
/// diagnostic running, an order being raised, a manager rejecting it, the reporter confirming
/// the repair held. Every service that fires one of those triggers calls <see cref="AdvanceAsync"/>
/// right after <see cref="WorkflowTransitions.Move"/>, before its own SaveChanges, so the report
/// and the workflow are written together or not at all — the same rule as ClarificationService
/// moving a report alongside its questions.
///
/// KEYED BY THE TRIGGER, NOT THE WORKFLOW'S NEW STATE, for the reason WorkflowTransitions is:
/// Closed is reached both by a manager rejecting the order and by the reporter confirming the
/// repair, and AwaitingManagerApproval both by an order needing a decision and by an escalation
/// — only the event says what happened to the fault.
///
/// EVERY MOVE GOES THROUGH THE REPORT LIFECYCLE (ReportService.CanMove). One the map refuses is
/// skipped and logged, never forced: a report a manager has already Closed stays Closed when a
/// run it no longer needs diagnoses it, and a reopened repair's second diagnosis does not drag a
/// WorkOrderRaised report back to Diagnosed. The move is a consequence; the workflow's own move
/// still happens.
///
/// The clarification moves are NOT here. AwaitingClarification is set with the questions and
/// REFUSES the questions when the lifecycle refuses it — a stronger rule than skip-and-log — and
/// Clarified is set with the answers, whether or not a workflow is found to move.
/// </summary>
public static class ReportProgress
{
    private static readonly IReadOnlyDictionary<WorkflowTrigger, ReportStatus> ImpliedStatus =
        new Dictionary<WorkflowTrigger, ReportStatus>
        {
            // The diagnostic has run — whether or not it produced a diagnosis, the fault has
            // been looked at and the strategist is proposing.
            [WorkflowTrigger.Diagnosed] = ReportStatus.Diagnosed,

            // An order is raised, on either side of the approval gate. Approval itself moves
            // nothing further: the order was already raised.
            [WorkflowTrigger.WorkOrderAutoApproved] = ReportStatus.WorkOrderRaised,
            [WorkflowTrigger.WorkOrderNeedsApproval] = ReportStatus.WorkOrderRaised,

            // The workflow is Closed, no order can be raised from it, and so nothing more will
            // happen on this report. A fault that still matters is a new report.
            [WorkflowTrigger.ManagerRejected] = ReportStatus.Closed,

            // The reporter said the repair held.
            [WorkflowTrigger.RepairVerified] = ReportStatus.Closed
        };

    /// <summary>Every trigger that moves a report, for a test to pin literally.</summary>
    public static IEnumerable<(WorkflowTrigger Trigger, ReportStatus To)> All =>
        ImpliedStatus.Select(p => (p.Key, p.Value));

    /// <summary>The report status <paramref name="trigger"/> implies, or null when it moves no report.</summary>
    public static ReportStatus? ImpliedBy(WorkflowTrigger trigger) =>
        ImpliedStatus.TryGetValue(trigger, out var to) ? to : null;

    /// <summary>
    /// Moves the workflow's report to the status <paramref name="trigger"/> implies, when the
    /// lifecycle allows it. NOT saved here: the caller's SaveChanges writes it with the
    /// workflow's move. Loads the report tracked, so one the caller already holds is the same
    /// instance. Returns whether the report moved.
    /// </summary>
    public static async Task<bool> AdvanceAsync(
        AppDbContext db,
        AgentWorkflow workflow,
        WorkflowTrigger trigger,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (ImpliedBy(trigger) is not { } to || workflow.ReportId is not int reportId)
        {
            return false;
        }

        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return false;
        }

        if (!ReportService.CanMove(report.Status, to))
        {
            logger.LogInformation(
                "Workflow {WorkflowId} fired {Trigger}, which would move report {ReportId} to {To}, "
                + "but the report is {From} and the report lifecycle does not allow that move. The "
                + "report is left where it is.",
                workflow.Id, trigger, reportId, to, report.Status);
            return false;
        }

        report.Status = to;
        return true;
    }

    /// <summary>
    /// The stage a REPORTER is shown. A pure function of three facts the API already holds,
    /// so the list, the detail and both clients read one answer.
    ///
    /// A Closed report reads NotGoingAhead when its latest order was rejected and Closed
    /// otherwise. Anything else follows the latest workflow; with none (the seeded history) it
    /// follows the report's own status. A Failed run reads BeingReviewed: the agents' failure
    /// costs advice, never the report — a manager can still raise the order.
    /// </summary>
    public static ReportStage StageFor(
        ReportStatus status,
        WorkflowState? latestWorkflow,
        bool latestOrderRejected)
    {
        if (status == ReportStatus.Closed)
        {
            return latestOrderRejected ? ReportStage.NotGoingAhead : ReportStage.Closed;
        }

        return latestWorkflow switch
        {
            null => status switch
            {
                ReportStatus.AwaitingClarification => ReportStage.WaitingOnYou,
                ReportStatus.WorkOrderRaised => ReportStage.RepairPlanned,
                _ => ReportStage.BeingReviewed
            },
            WorkflowState.AwaitingClarification => ReportStage.WaitingOnYou,
            WorkflowState.AwaitingManagerApproval => ReportStage.AwaitingApproval,
            WorkflowState.WorkOrderRaised or WorkflowState.InProgress => ReportStage.RepairPlanned,
            WorkflowState.Completed or WorkflowState.AwaitingVerification => ReportStage.Repaired,
            // Closed with the report still open: the report's own move was skipped. The
            // workflow's end still says what happened — rejected, or repaired and verified.
            WorkflowState.Closed => latestOrderRejected ? ReportStage.NotGoingAhead : ReportStage.Repaired,
            // Submitted, Diagnosing, Strategizing, Failed.
            _ => ReportStage.BeingReviewed
        };
    }
}
