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
    /// How long the API waits for one POST /run before giving up.
    ///
    /// Generous on purpose. A measured clarifier run against a real provider takes about
    /// ten seconds, and the agent's own LLM timeout is 30s with one retry, so a slow-but-
    /// working run can legitimately approach a minute. This timeout exists to stop a
    /// wedged agent pinning a background worker forever — not to second-guess a slow one,
    /// which is why it is deliberately longer than anything the agent should ever need.
    /// </summary>
    public double TimeoutSeconds { get; init; } = 60;
}
