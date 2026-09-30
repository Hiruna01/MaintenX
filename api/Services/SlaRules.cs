using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The repair SLA, written once: an approved work order must be completed within
/// SlaSettings.ResolutionDays of the moment it was approved.
///
/// A deterministic business rule, so it is C# — never a prompt, and never the strategist's
/// "urgency", which is advice nothing acts on. Pure functions over instants, like
/// FailureRules and SlotRules; "now" is the caller's, read from TimeProvider.
///
/// THE CLOCK STARTS AT APPROVAL, not when the fault was reported or the order raised. Time
/// spent waiting for a manager's decision is not time the maintenance team could have spent
/// on the job, and an SLA that counted it would be breached by the approval step itself.
///
/// DueAt is STAMPED on the order when the clock starts (WorkOrderService), not recomputed on
/// every read — the same decision as VerificationCheck.DueAt: changing ResolutionDays later
/// moves no promise already made.
/// </summary>
public static class SlaRules
{
    /// <summary>When an order approved at <paramref name="approvedAt"/> is due.</summary>
    public static DateTime DueAt(DateTime approvedAt, int resolutionDays) =>
        approvedAt.AddDays(resolutionDays);

    /// <summary>
    /// Where an order stands against its due time.
    ///
    /// Exactly ON the due time is still on time — both while live (strictly after is overdue)
    /// and at completion (at or before is met). Same shape as the approval threshold: the
    /// limit itself is inside the limit.
    /// </summary>
    public static SlaState StateOf(
        WorkOrderStatus status,
        DateTime? dueAt,
        DateTime? completedAt,
        DateTime now)
    {
        if (dueAt is null)
        {
            return SlaState.None;
        }

        switch (status)
        {
            case WorkOrderStatus.Completed:
                // A completed order always has CompletedAt; a row without one says nothing.
                return completedAt is null
                    ? SlaState.None
                    : completedAt <= dueAt ? SlaState.Met : SlaState.Missed;

            case WorkOrderStatus.Approved:
            case WorkOrderStatus.Scheduled:
            case WorkOrderStatus.InProgress:
                return now > dueAt ? SlaState.Overdue : SlaState.OnTrack;

            default:
                // Draft / AwaitingApproval never have a DueAt (the clock starts at approval,
                // and an approved order cannot go back); Rejected / Cancelled have nothing
                // left to be late for.
                return SlaState.None;
        }
    }
}
