using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

/// <summary>
/// The API's outbound half of the machine-to-machine channel: this calls POST /run on the
/// Python agent service. (The inbound half is InternalToolsController, which the agent
/// calls back into for data.)
/// </summary>
public interface IAgentClient
{
    /// <summary>
    /// Runs the agent for one workflow.
    ///
    /// NEVER THROWS. A timeout, a refused connection, a non-200 or an unparseable body all
    /// come back as <see cref="AgentCallResult.Ok"/> false with a reason — the only caller
    /// is a background worker, and an exception there has no request to surface on.
    /// </summary>
    Task<AgentCallResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of one call to the agent service. <c>Ok</c> means the call completed and the
/// body parsed — it does NOT mean the agent produced useful output, which is
/// <see cref="AgentRunResponse.IsSafeFailure"/>.
/// </summary>
public record AgentCallResult(bool Ok, AgentRunResponse? Response, string? Error, int DurationMs);
