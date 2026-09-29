namespace CampusFacilities.Api.Services;

/// <summary>
/// Configuration for the machine-to-machine channel with the Python agent service.
///
/// The agent has no database credentials by design — it reads campus data only through
/// the allow-listed tool endpoints on this API. Those endpoints are not for humans, so
/// they are not protected by a JWT: there is no user to authenticate, no role to check
/// and no login the agent could perform. A single shared secret in a request header is
/// the honest description of "one trusted back-end process calling another", and it is
/// the whole reason InternalToolsController does not sit behind [Authorize].
///
/// The value comes from configuration (Agent:SharedSecret, or AGENT_SHARED_SECRET from
/// the root .env) — never a literal in code.
/// </summary>
public class AgentSettings
{
    /// <summary>Header the agent service must send its shared secret in.</summary>
    public const string SecretHeaderName = "X-Agent-Secret";

    public string SharedSecret { get; init; } = string.Empty;

    /// <summary>
    /// Base URL of the Python agent service, e.g. http://localhost:8000. Empty disables
    /// the outbound call: the runner records why and fails the workflow rather than
    /// throwing, so an API running without the agent is degraded, not broken.
    /// From configuration (Agent:BaseUrl, or AGENT_SERVICE_URL from the root .env).
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// How long the API waits for one POST /run before giving up, by default.
    ///
    /// BUDGETED against the agent's own timeouts, not guessed. One /run on a fresh report can
    /// run four agents in a row — planner, clarifier, diagnostic, strategist — and each makes
    /// at most two LLM attempts at LLM_TIMEOUT_SECONDS (30s), plus its tool calls at
    /// TOOL_TIMEOUT_SECONDS (10s) each:
    ///
    ///   planner     2 × 30s                 =  60s   (no tools)
    ///   clarifier   2 × 30s + 2 tools × 10s =  80s
    ///   diagnostic  2 × 30s + 3 tools × 10s =  90s
    ///   strategist  2 × 30s + 3 tools × 10s =  90s
    ///                                         ─────
    ///                                          320s
    ///
    /// 360s sits above that worst case. When this was 60s, a slow provider made the API mark
    /// the run Failed while the agent was still working, and the agent's later tool calls
    /// kept writing audit rows onto a workflow already declared dead. A real run takes tens
    /// of seconds; this only has to stop a WEDGED agent pinning the runner forever. If either
    /// agent timeout is raised, raise this with it.
    /// </summary>
    public const double DefaultTimeoutSeconds = 360;

    /// <summary>How long the API waits for one POST /run. See <see cref="DefaultTimeoutSeconds"/>.</summary>
    public double TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
}
