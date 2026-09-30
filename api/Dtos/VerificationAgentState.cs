namespace CampusFacilities.Api.Dtos;

/// <summary>
/// Where the VerificationAgent's review of one check has got to. Derived in C# by
/// <see cref="Services.VerificationAgentRules.StateOf"/> from the check's queue and judgement
/// stamps — never stored, so it lives beside the DTOs like <see cref="ReportStage"/> — so that
/// neither client compares two timestamps to decide what to show.
///
/// It says nothing about whether the repair held. That is the check's Status, set from the
/// reporter's answer; the agent's verdict is advice beside it.
///
/// Sent and accepted by NAME, like every other enum in this API.
/// </summary>
public enum VerificationAgentState
{
    /// <summary>Not handed to the agent: the reporter has not answered, nor been silent past the window.</summary>
    NotQueued,

    /// <summary>Handed to the agent and waiting for its run.</summary>
    Queued,

    /// <summary>The agent service could not be reached last time; it will be tried again. AgentError says why.</summary>
    Retrying,

    /// <summary>The agent gave its verdict — AgentOutcome, AgentReason and AgentEvidence.</summary>
    Judged,

    /// <summary>The agent could not give a verdict and will not be asked again. AgentError says why.</summary>
    CouldNotJudge
}
