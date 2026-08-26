using System.Security.Cryptography;
using System.Text;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CampusFacilities.Api.Middleware;

/// <summary>
/// Authenticates the Python agent service by a shared secret header instead of a JWT.
///
/// This is an authorization FILTER rather than a check at the top of the action on
/// purpose: filters run before [ApiController]'s automatic model validation, so a caller
/// without the secret always gets 401 and never a 400 that would tell them what the body
/// is supposed to look like.
///
/// Applied with [ServiceFilter] so it gets constructor injection like everything else —
/// an attribute cannot take DI arguments, and pulling AgentSettings out of the container
/// by hand would be the service locator this project does not use.
/// </summary>
public class AgentSecretFilter : IAuthorizationFilter
{
    private readonly AgentSettings _agentSettings;
    private readonly ILogger<AgentSecretFilter> _logger;

    public AgentSecretFilter(AgentSettings agentSettings, ILogger<AgentSecretFilter> logger)
    {
        _agentSettings = agentSettings;
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var provided = context.HttpContext.Request.Headers[AgentSettings.SecretHeaderName].ToString();

        if (SecretMatches(provided))
        {
            return;
        }

        // Path only — never the header value, and never the body.
        _logger.LogWarning(
            "Rejected an internal tool call to {Path} with a missing or incorrect agent secret.",
            context.HttpContext.Request.Path);

        context.Result = new UnauthorizedResult();
    }

    private bool SecretMatches(string? provided)
    {
        // No configured secret means closed, not open. An unset AGENT_SHARED_SECRET must
        // never turn these endpoints into anonymous read access to campus data.
        if (string.IsNullOrEmpty(_agentSettings.SharedSecret) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Fixed-time comparison so a caller cannot recover the secret byte by byte from
        // how long the comparison took.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_agentSettings.SharedSecret),
            Encoding.UTF8.GetBytes(provided));
    }
}
