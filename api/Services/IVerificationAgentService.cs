namespace CampusFacilities.Api.Services;

/// <summary>
/// The C# side of the VerificationAgent: it hands queued checks to the agent service and
/// writes back what the agent said. The body of VerificationAgentRunner's pass lives here, in a
/// scoped service, for the same reason the sweep's lives in VerificationService — testable
/// without a background worker, and no scoped DbContext held by a singleton.
/// </summary>
public interface IVerificationAgentService
{
    /// <summary>
    /// ONE PASS over every check waiting on the agent (VerificationAgentRules.AwaitingJudgement),
    /// oldest queued first, ONE AT A TIME, each saved on its own:
    ///
    ///   * the run belongs to the latest workflow of the check's report — its AgentStep is
    ///     written there ("verification", "[]" tool calls, output verbatim, the agent's own
    ///     duration and attempts), and the agent's tool calls are recorded there too;
    ///   * a verdict writes AgentOutcome, AgentReason and AgentEvidenceJson, and NEVER Status —
    ///     the check's status is the reporter's answer, set in C#;
    ///   * a call that failed is retried on later passes, up to VerificationAgentRules.MaxAttempts
    ///     calls, each counted before it is made; a safe failure is final at once; a report
    ///     with no workflow is given up on without a call. Given up means AgentJudgedAt stamped,
    ///     AgentOutcome null and AgentError saying why — never retried forever.
    ///
    /// A row that throws is logged and left for the next pass; only cancellation escapes. A
    /// static lock stops the timer and a wake running two passes at once.
    /// </summary>
    Task<VerificationAgentPassResult> JudgeQueuedChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True while the agent may still be working for this workflow: it is the LATEST workflow of
    /// its report and a check on that report is waiting on the agent. The tool router's one
    /// exception to "an ended workflow takes no more tool calls" — a confirmed repair's workflow
    /// is Closed, and the agent still has to read the repair it is judging.
    /// </summary>
    Task<bool> IsJudgingOnWorkflowAsync(int workflowId, CancellationToken cancellationToken = default);
}

/// <summary>What one pass did, for the log. Not a response DTO — nothing serialises it.</summary>
public record VerificationAgentPassResult(int Judged, int Retrying, int GaveUp, int Failed);
