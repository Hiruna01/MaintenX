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
}
