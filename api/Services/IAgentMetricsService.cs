using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public interface IAgentMetricsService
{
    /// <summary>
    /// Runs, failures, retries, latency and reported tokens per agent, a daily series, and the
    /// slowest and costliest runs — every figure read off the agent-level AgentStep rows. See
    /// AgentMetricsDto.
    ///
    /// <paramref name="fromDate"/> and <paramref name="toDate"/> are UTC calendar days on the
    /// step's CreatedAt, both ends inclusive, either may be omitted. The caller checks
    /// from &lt;= to. No agent is called.
    /// </summary>
    Task<AgentMetricsDto> GetAgentMetricsAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default);
}
