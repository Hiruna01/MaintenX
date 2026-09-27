using System.Linq.Expressions;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

/// <summary>
/// When a verification check is waiting on the VerificationAgent, and how many times the
/// agent is asked before the system stops asking. Written once, because three readers need
/// them: the runner picking its work (VerificationAgentService), the tool router deciding
/// whether an ended workflow may still take the agent's calls, and the detail DTO's
/// <see cref="VerificationAgentState"/>.
///
/// Pure functions over the check's columns — no database, no clock.
/// </summary>
public static class VerificationAgentRules
{
    /// <summary>
    /// Agent calls per queue stamp. A call the agent service never answered is tried again on a
    /// later pass, up to this many in all; then the check is given up on with the reason. A
    /// SAFE FAILURE is not retried at all — the agent already retried its model once inside the
    /// call, and asking again costs two more model calls for what is most often the same answer.
    ///
    /// A constant rather than configuration, like the tool row caps: it is a bound on how much
    /// one row can cost, not a policy anybody tunes.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// A check is WAITING ON THE AGENT while it is queued and has not been judged since. Judged
    /// includes given up on: <see cref="VerificationCheck.AgentJudgedAt"/> is stamped either way,
    /// which is what stops a failing row being retried forever.
    ///
    /// "Since" is what makes a late answer work. A check judged as silent and answered later is
    /// stamped queued again, after its judgement, so it is waiting again.
    ///
    /// An expression, so EF translates it — the runner's pick and the router's check query it.
    /// </summary>
    public static readonly Expression<Func<VerificationCheck, bool>> AwaitingJudgement =
        v => v.AgentQueuedAt != null && (v.AgentJudgedAt == null || v.AgentJudgedAt < v.AgentQueuedAt);

    private static readonly Func<VerificationCheck, bool> IsAwaitingJudgement = AwaitingJudgement.Compile();

    /// <summary>What the detail DTO reports about the agent's review. See <see cref="VerificationAgentState"/>.</summary>
    public static VerificationAgentState StateOf(VerificationCheck check)
    {
        if (IsAwaitingJudgement(check))
        {
            return check.AgentError is null ? VerificationAgentState.Queued : VerificationAgentState.Retrying;
        }

        if (check.AgentJudgedAt is null)
        {
            return VerificationAgentState.NotQueued;
        }

        return check.AgentOutcome is null ? VerificationAgentState.CouldNotJudge : VerificationAgentState.Judged;
    }
}
