using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using CampusFacilities.Api.Dtos;

namespace CampusFacilities.Api.Services;

public class AgentClient : IAgentClient
{
    private readonly HttpClient _http;
    private readonly AgentSettings _settings;
    private readonly ILogger<AgentClient> _logger;

    public AgentClient(HttpClient http, AgentSettings settings, ILogger<AgentClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AgentCallResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            // Not an exception: an API deployed without the agent should degrade to
            // "workflows fail with a clear reason", not "the background worker crashes".
            stopwatch.Stop();
            return new AgentCallResult(
                Ok: false,
                Response: null,
                Error: "No agent service URL configured (Agent:BaseUrl / AGENT_SERVICE_URL).",
                DurationMs: (int)stopwatch.ElapsedMilliseconds);
        }

        try
        {
            var response = await _http.PostAsJsonAsync("/run", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Body deliberately not logged or returned: it is a remote service's error
                // text, and this string ends up in a column a browser renders.
                stopwatch.Stop();
                return Failure($"The agent service returned HTTP {(int)response.StatusCode}.", stopwatch);
            }

            var body = await response.Content.ReadFromJsonAsync<AgentRunResponse>(
                cancellationToken: cancellationToken);

            stopwatch.Stop();

            return body is null
                ? Failure("The agent service returned an empty body.", stopwatch)
                : new AgentCallResult(true, body, null, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as TaskCanceledException. The `when`
            // separates it from a genuine shutdown, which must stay a cancellation.
            stopwatch.Stop();
            return Failure(
                $"The agent service did not respond within {_settings.TimeoutSeconds:0} seconds.",
                stopwatch);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return Failure($"Could not reach the agent service: {ex.Message}", stopwatch);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            return Failure($"The agent service returned a body that could not be read: {ex.Message}", stopwatch);
        }
    }

    private AgentCallResult Failure(string error, Stopwatch stopwatch)
    {
        _logger.LogWarning("Agent call failed: {Error}", error);
        return new AgentCallResult(false, null, error, (int)stopwatch.ElapsedMilliseconds);
    }
}
